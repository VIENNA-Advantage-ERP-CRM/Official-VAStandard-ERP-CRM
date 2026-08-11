/// <summary>
/// Module Name : VASLogic
/// Purpose     : Product Overview right tab panel data (read side). Returns a
///               read-only contextual summary of the selected M_Product:
///               identity + lifecycle hero, attribute-set controls, tax
///               classification, stock position and stock by locator, UOM
///               conversions, price lists, BOMs (own + where-used), configured
///               quality parameters, vendors, the latest sales / purchase
///               orders, recent physical movements, the effective posting
///               accounts and a unified activity timeline.
///
///               Every section is its own small query. One combined statement
///               would multiply rows across the panel's many one-to-many
///               relationships and make every figure on screen unreliable.
///
///               MRole access filtering is applied to the MAIN physical table
///               of each query — the alias the user is actually fetching from —
///               and never to a derived table, a CTE alias or a secondary join
///               used only for lookup. Where a query needs a top-N or an
///               aggregate, AddAccessSQL is applied to the inner statement
///               BEFORE it is wrapped or before GROUP BY / ORDER BY is
///               appended, because the rewriter appends its predicate to the
///               end of the WHERE clause it is given.
///
///               Portability: the SQL avoids NVL / DECODE / ROWNUM / LIMIT /
///               FETCH FIRST / LISTAGG and uses COALESCE, CASE, ANSI joins and
///               ROW_NUMBER, so one statement serves both Oracle and
///               PostgreSQL. Free-text literals carry the national-character
///               prefix on Oracle through NLiteral; stored code comparisons
///               (IsActive = 'Y', DocStatus = 'CO') deliberately do not.
///
///               Every optional module column (VA010 quality, the discontinued
///               flag, the resource-absorption account) is resolved through the
///               dictionary first, so the panel works whether or not those
///               modules are installed.
/// Chronological development:
///   VAI163   2026-08-10  Created.
///   VAI163   2026-08-10  - The product image is resolved to something the browser
///                          can render (ResolveImageUrl): an absolute ImageURL,
///                          else a file that actually exists under the server's
///                          Images folder, else AD_Image.BinaryData inlined as a
///                          data URI. The stored ImageURL alone is usually a bare
///                          file name, so returning it rendered nothing. This is
///                          the resolution VAS_078_ProductSearchWidget already
///                          performs, reused rather than reimplemented.
///                        - The activity trail no longer applies MRole to its
///                          sources, which is why it came back empty. Those five
///                          queries are children of a product row already read
///                          under the access filter and are pinned to it by
///                          AD_Table_ID + Record_ID; on audit / correspondence
///                          tables the SQL_FULLYQUALIFIED rewriter either matched
///                          nothing or reached for a column that is not there.
///                          VAS_092 and VAS_099 apply it to the document only.
///                        - Appointments are de-duplicated on (StartDate,
///                          Subject): AppointmentsInfo stores one row per
///                          ATTENDEE, so a five-person meeting filled the
///                          timeline with five identical entries.
///                        - Recent transactions carry the source document's own
///                          C_DocType name and the table + id the panel navigates
///                          to (M_InOut / M_Inventory / M_Movement).
///   VAI163   2026-08-10  Activity gained the chat source (CM_Chat / CM_ChatEntry),
///                        which was missing from the trail entirely. It is
///                        separate from AD_Note — a note is system-raised, a chat
///                        entry is somebody typing on the record — and its author
///                        falls back from AD_User_ID to CreatedBy, which is what
///                        the platform's own chat plumbing leaves set.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_190_ProductOverviewRightPanelModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_190_ProductOverviewRightPanelModel).FullName);

        /// <summary>The four supported product types. Nothing else is queried.</summary>
        private const string TYPE_ITEM     = "I";
        private const string TYPE_SERVICE  = "S";
        private const string TYPE_RESOURCE = "R";
        private const string TYPE_EXPENSE  = "E";

        /// <summary>Latest-N caps. The panel is contextual: it shows recent history, not all of it.</summary>
        private const int MAX_ORDERS       = 5;
        private const int MAX_TRANSACTIONS = 20;
        private const int MAX_ACTIVITY     = 60;

        /// <summary>M_Product's AD_Table_ID, resolved once per request and inlined
        /// into the activity queries as an integer literal. Inlining keeps each
        /// statement down to a single bind name, which positional binding needs.</summary>
        private int _productTableId;

        // ----------------------------------------------------------------- //
        //  Entry point                                                       //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Returns the full overview payload for the selected product.
        /// The product row itself is read under MRole, so an id the browser sent
        /// for a record the role cannot see comes back as an empty payload — the
        /// id is never trusted on its own.
        /// </summary>
        /// <param name="ctx">User context (client / org / role).</param>
        /// <param name="M_Product_ID">Selected product id.</param>
        /// <returns>Populated <see cref="ProductOverviewData"/>; an empty instance
        /// when the id is invalid or no accessible row is found.</returns>
        public ProductOverviewData GetProductOverview(Ctx ctx, int M_Product_ID)
        {
            ProductOverviewData result = new ProductOverviewData();
            if (ctx == null || M_Product_ID <= 0) return result;

            result.Product = LoadSummary(ctx, M_Product_ID);
            if (result.Product == null || result.Product.M_Product_ID <= 0)
                return result;   // not accessible to this role, or does not exist

            string type = result.Product.ProductType;
            bool isItem = type == TYPE_ITEM;

            _productTableId = GetTableId("M_Product");

            // ----- Sections that apply to every product type -----
            result.Attributes     = LoadAttributes(ctx, result.Product.M_AttributeSet_ID);
            result.Tax            = LoadTax(ctx, M_Product_ID);
            result.UomConversions = LoadUomConversions(ctx, M_Product_ID, result.Product.BaseUomName);
            result.Pricing        = LoadPricing(ctx, M_Product_ID);
            result.Suppliers      = LoadSuppliers(ctx, M_Product_ID);
            result.SalesOrders    = LoadOrders(ctx, M_Product_ID, true);
            result.PurchaseOrders = LoadOrders(ctx, M_Product_ID, false);

            // ----- Item-only sections -----
            if (isItem)
            {
                result.StockSummary = LoadStockSummary(ctx, M_Product_ID);
                result.StockDetails = LoadStockDetails(ctx, M_Product_ID);
                // The Manufacturing section is gated on the product's own BOM
                // flag; the where-used rows ride along inside it.
                if (result.Product.IsBOM)
                {
                    result.Manufacturing = LoadBoms(ctx, M_Product_ID);
                }
                if (result.Product.VA010_QualityPlan_ID > 0)
                {
                    result.Quality = LoadQualityParams(ctx, result.Product.VA010_QualityPlan_ID);
                }
                result.Transactions = LoadTransactions(ctx, M_Product_ID);
            }

            // ----- Accounting: per type, and only when an account resolves -----
            result.Accounting = LoadAccounting(ctx, M_Product_ID,
                                               result.Product.M_Product_Category_ID, type);

            // ----- Activity: merged from its sources, newest first -----
            result.Activity = LoadActivity(ctx, M_Product_ID);

            return result;
        }

        // ----------------------------------------------------------------- //
        //  1. Product summary (hero)                                         //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Reads the product's identity, category, base UOM, image reference and
        /// lifecycle state. This is the query that decides whether the caller may
        /// see the record at all, so MRole is applied here on M_Product.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="M_Product_ID">Selected product id.</param>
        /// <returns>The summary, or null when no accessible row exists.</returns>
        private ProductSummaryData LoadSummary(Ctx ctx, int M_Product_ID)
        {
            // Optional columns — resolved through the dictionary so a schema
            // without them degrades instead of failing the whole panel.
            bool hasDiscontinued   = ColumnExists("M_Product", "Discontinued");
            bool hasDiscontinuedBy = ColumnExists("M_Product", "DiscontinuedBy");
            bool hasQualityPlan    = ColumnExists("M_Product", "VA010_QualityPlan_ID");
            bool hasImageUrl       = ColumnExists("M_Product", "ImageURL");

            string discontinuedExpr   = hasDiscontinued
                ? "COALESCE(p.Discontinued, 'N')" : "'N'";
            string discontinuedAtExpr = hasDiscontinuedBy
                ? "p.DiscontinuedBy" : "CAST(NULL AS DATE)";
            string qualityPlanExpr    = hasQualityPlan
                ? "COALESCE(p.VA010_QualityPlan_ID, 0)" : "0";
            // The product's own image URL wins over the AD_Image record's. Both
            // are only the STORED value — what the browser can actually render
            // is worked out afterwards by ResolveImageUrl.
            string imageUrlExpr = hasImageUrl
                ? "COALESCE(p.ImageURL, img.ImageURL)" : "img.ImageURL";

            string sql = @"SELECT p.M_Product_ID,
                                  p.Name AS ProductName,
                                  p.Value AS ProductCode,
                                  p.SKU,
                                  p.UPC,
                                  p.ProductType,
                                  p.IsActive,
                                  p.IsBOM,
                                  p.IsVerified,
                                  p.M_Product_Category_ID,
                                  p.M_AttributeSet_ID,
                                  p.C_UOM_ID,
                                  p.AD_Image_ID,
                                  " + discontinuedExpr + @" AS Discontinued,
                                  " + discontinuedAtExpr + @" AS DiscontinuedFrom,
                                  " + qualityPlanExpr + @" AS VA010_QualityPlan_ID,
                                  " + imageUrlExpr + @" AS ImageUrl,
                                  img.ImageExtension,
                                  pc.Name AS CategoryName,
                                  u.Name AS BaseUomName,
                                  COALESCE(u.StdPrecision, 0) AS UomPrecision
                           FROM M_Product p
                           LEFT OUTER JOIN M_Product_Category pc ON (pc.M_Product_Category_ID=p.M_Product_Category_ID)
                           LEFT OUTER JOIN C_UOM u ON (u.C_UOM_ID=p.C_UOM_ID)
                           LEFT OUTER JOIN AD_Image img ON (img.AD_Image_ID=p.AD_Image_ID)
                           WHERE p.M_Product_ID=@M_Product_ID
                             AND p.IsActive IN ('Y','N')";

            // The one query whose access filter decides visibility of the record.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadSummary");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return null;

            DataRow r = ds.Tables[0].Rows[0];
            ProductSummaryData s = new ProductSummaryData();
            s.M_Product_ID          = Util.GetValueOfInt(r["M_Product_ID"]);
            s.Name                  = Util.GetValueOfString(r["ProductName"]);
            s.Code                  = Util.GetValueOfString(r["ProductCode"]);
            s.SKU                   = Util.GetValueOfString(r["SKU"]);
            s.Barcode               = Util.GetValueOfString(r["UPC"]);
            s.ProductType           = Util.GetValueOfString(r["ProductType"]);
            s.IsActiveRecord        = Util.GetValueOfString(r["IsActive"]) == "Y";
            s.IsBOM                 = Util.GetValueOfString(r["IsBOM"]) == "Y";
            s.IsVerified            = Util.GetValueOfString(r["IsVerified"]) == "Y";
            s.IsDiscontinued        = Util.GetValueOfString(r["Discontinued"]) == "Y";
            s.DiscontinuedFrom      = Util.GetValueOfDateTime(r["DiscontinuedFrom"]);
            s.M_Product_Category_ID = Util.GetValueOfInt(r["M_Product_Category_ID"]);
            s.CategoryName          = Util.GetValueOfString(r["CategoryName"]);
            s.M_AttributeSet_ID     = Util.GetValueOfInt(r["M_AttributeSet_ID"]);
            s.C_UOM_ID              = Util.GetValueOfInt(r["C_UOM_ID"]);
            s.BaseUomName           = Util.GetValueOfString(r["BaseUomName"]);
            s.UomPrecision          = Util.GetValueOfInt(r["UomPrecision"]);
            s.AD_Image_ID           = Util.GetValueOfInt(r["AD_Image_ID"]);
            s.VA010_QualityPlan_ID  = Util.GetValueOfInt(r["VA010_QualityPlan_ID"]);

            // What the browser can actually render, which is rarely the stored
            // value on its own — see ResolveImageUrl.
            s.ImageUrl = ResolveImageUrl(ctx, s.AD_Image_ID,
                                         Util.GetValueOfString(r["ImageExtension"]),
                                         Util.GetValueOfString(r["ImageUrl"]));

            // Status precedence, exactly as specified: discontinued wins over
            // active, and there is no fourth lifecycle state.
            if (s.IsDiscontinued)      s.StatusCode = "DISCONTINUED";
            else if (s.IsActiveRecord) s.StatusCode = "ACTIVE";
            else                       s.StatusCode = "INACTIVE";

            // Only the four supported types are recognised; anything else is left
            // as read so the panel can show the raw code rather than mislabel it.
            if (s.ProductType != TYPE_ITEM && s.ProductType != TYPE_SERVICE
                && s.ProductType != TYPE_RESOURCE && s.ProductType != TYPE_EXPENSE)
            {
                _log.Info("VAS_190: unsupported ProductType '" + s.ProductType
                          + "' on M_Product_ID=" + M_Product_ID
                          + " — item-only sections are not rendered.");
            }
            return s;
        }

        // ----------------------------------------------------------------- //
        //  Product image                                                     //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Resolves a product's AD_Image record to something the browser can
        /// actually render. The stored ImageURL alone is usually not it — on most
        /// installations it is a bare file name, and on some the picture only
        /// exists as bytes in the database.
        ///
        ///   1. An ImageURL that is already absolute (http / https / data:) is
        ///      returned untouched — the image is hosted elsewhere.
        ///   2. A file uploaded to this web server under GlobalVariable.ImagePath.
        ///      The original is preferred (the hero box is 3.5em square and a
        ///      thumbnail would show soft), then progressively smaller
        ///      thumbnails. Only a path whose file actually EXISTS is returned,
        ///      so the panel never renders a broken image.
        ///   3. AD_Image.BinaryData, inlined as a base64 data URI.
        ///
        /// Null when there is nothing to show, and the panel falls back to its
        /// dashed placeholder. The client prepends VIS.Application.contextUrl to
        /// a relative result.
        ///
        /// This is the same resolution VAS_078_ProductSearchWidget performs —
        /// the one place in this solution that already renders product images —
        /// rather than a second implementation that would drift from it.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="adImageId">M_Product.AD_Image_ID; 0 when the product has none.</param>
        /// <param name="imageExtension">AD_Image.ImageExtension (e.g. ".png").</param>
        /// <param name="storedImageUrl">The stored ImageURL, product's or image record's.</param>
        /// <returns>A renderable URL or data URI, or null when there is no image.</returns>
        private string ResolveImageUrl(Ctx ctx, int adImageId, string imageExtension, string storedImageUrl)
        {
            if (ctx == null || adImageId <= 0) return null;

            try
            {
                // 1. Hosted elsewhere, or already inlined.
                if (!string.IsNullOrEmpty(storedImageUrl)
                    && (storedImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || storedImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        || storedImageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
                {
                    return storedImageUrl;
                }

                // 2. A file this web server is holding.
                string fileName = GetImageFileName(adImageId, imageExtension, storedImageUrl);
                if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(GlobalVariable.ImagePath))
                {
                    string[] subFolders = { "", "Thumb500x375", "Thumb320x240",
                                            "Thumb320x185", "Thumb140x120", "Thumb46x46" };
                    for (int i = 0; i < subFolders.Length; i++)
                    {
                        string folder = subFolders[i].Length == 0
                            ? GlobalVariable.ImagePath
                            : System.IO.Path.Combine(GlobalVariable.ImagePath, subFolders[i]);
                        if (System.IO.File.Exists(System.IO.Path.Combine(folder, fileName)))
                        {
                            return subFolders[i].Length == 0
                                ? "Images/" + fileName
                                : "Images/" + subFolders[i] + "/" + fileName;
                        }
                    }
                }

                // 3. Bytes in the database. This is the one place the panel does
                //    read image binary, and only for the ONE product on screen —
                //    never across a list.
                MImage image = MImage.Get(ctx, adImageId);
                if (image != null)
                {
                    byte[] imageData = image.GetBinaryData();
                    if (imageData != null && imageData.Length > 0)
                    {
                        return "data:" + GetImageMimeType(imageExtension, imageData)
                             + ";base64," + Convert.ToBase64String(imageData);
                    }
                }
            }
            catch (Exception ex)
            {
                // A missing file, a bad path or an unreadable BLOB costs the
                // picture, never the panel.
                _log.Severe("VAS_190 ResolveImageUrl(AD_Image_ID=" + adImageId + "): " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// The uploaded image's file name: what ImageURL stores, stripped of any
        /// folder part so the value can never escape the Images directory, else
        /// the upload convention &lt;AD_Image_ID&gt;&lt;extension&gt;.
        /// </summary>
        private string GetImageFileName(int adImageId, string imageExtension, string storedImageUrl)
        {
            if (!string.IsNullOrEmpty(storedImageUrl))
            {
                try
                {
                    string fileName = System.IO.Path.GetFileName(storedImageUrl.Trim());
                    if (!string.IsNullOrEmpty(fileName)) return fileName;
                }
                catch (ArgumentException)
                {
                    // Invalid path characters — fall through to the convention.
                }
            }
            return !string.IsNullOrEmpty(imageExtension) ? adImageId + imageExtension : null;
        }

        /// <summary>
        /// The MIME type for an inlined image. The stored extension is unreliable
        /// for a BLOB, so the magic bytes are sniffed first and the extension is
        /// only the fallback.
        /// </summary>
        private string GetImageMimeType(string imageExtension, byte[] data)
        {
            if (data != null && data.Length > 11)
            {
                if (data[0] == 0xFF && data[1] == 0xD8) return "image/jpeg";
                if (data[0] == 0x89 && data[1] == 0x50) return "image/png";
                if (data[0] == 0x47 && data[1] == 0x49) return "image/gif";
                if (data[0] == 0x42 && data[1] == 0x4D) return "image/bmp";
                if (data[0] == 0x52 && data[1] == 0x49 && data[8] == 0x57 && data[9] == 0x45)
                    return "image/webp";
            }

            switch ((imageExtension ?? "").Trim().ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif":  return "image/gif";
                case ".bmp":  return "image/bmp";
                case ".webp": return "image/webp";
                default:      return "image/png";
            }
        }

        // ----------------------------------------------------------------- //
        //  2. Attributes                                                     //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The controls the product's attribute set imposes on transactions —
        /// lot, serial number and guarantee/expiry date — followed by the set's
        /// instance attributes with the number of values each defines.
        /// Only controls the set actually switches on are returned; nothing is
        /// inferred, and no flag is invented to fill a row.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="M_AttributeSet_ID">The product's attribute set; 0 when none.</param>
        /// <returns>Attribute rows, or an empty list when the set has no controls.</returns>
        private List<AttributeRowData> LoadAttributes(Ctx ctx, int M_AttributeSet_ID)
        {
            List<AttributeRowData> rows = new List<AttributeRowData>();
            if (M_AttributeSet_ID <= 0) return rows;

            // --- The set's own control flags ---
            string setSql = @"SELECT aset.M_AttributeSet_ID,
                                     aset.Name AS AttributeSetName,
                                     COALESCE(aset.IsLot, 'N') AS IsLot,
                                     COALESCE(aset.IsSerNo, 'N') AS IsSerNo,
                                     COALESCE(aset.IsGuaranteeDate, 'N') AS IsGuaranteeDate,
                                     COALESCE(aset.GuaranteeDays, 0) AS GuaranteeDays,
                                     COALESCE(aset.IsInstanceAttribute, 'N') AS IsInstanceAttribute
                              FROM M_AttributeSet aset
                              WHERE aset.M_AttributeSet_ID=@M_AttributeSet_ID
                                AND aset.IsActive='Y'";
            setSql = MRole.GetDefault(ctx).AddAccessSQL(
                setSql, "aset", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(setSql,
                new SqlParameter[] { new SqlParameter("@M_AttributeSet_ID", M_AttributeSet_ID) },
                "LoadAttributes(set)");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return rows;

            DataRow r = ds.Tables[0].Rows[0];
            string setName = Util.GetValueOfString(r["AttributeSetName"]);

            if (Util.GetValueOfString(r["IsLot"]) == "Y")
            {
                rows.Add(new AttributeRowData
                {
                    Name    = "LOT",
                    Kind    = "control",
                    ChipKey = "ON"
                });
            }
            if (Util.GetValueOfString(r["IsSerNo"]) == "Y")
            {
                rows.Add(new AttributeRowData
                {
                    Name    = "SERNO",
                    Kind    = "control",
                    ChipKey = "ON"
                });
            }
            if (Util.GetValueOfString(r["IsGuaranteeDate"]) == "Y")
            {
                rows.Add(new AttributeRowData
                {
                    Name          = "GUARANTEEDATE",
                    Kind          = "control",
                    ChipKey       = "ON",
                    GuaranteeDays = Util.GetValueOfInt(r["GuaranteeDays"])
                });
            }

            // --- The set's instance attributes, with their value counts ---
            // COUNT of the attribute's list values is a correlated scalar
            // subquery, never a join: a join would repeat the attribute row once
            // per value and the panel would list the same control several times.
            string attrSql = @"SELECT a.M_Attribute_ID,
                                      a.Name AS AttributeName,
                                      a.AttributeValueType,
                                      COALESCE(a.IsMandatory, 'N') AS IsMandatory,
                                      (SELECT COUNT(1) FROM M_AttributeValue av
                                        WHERE av.M_Attribute_ID=a.M_Attribute_ID
                                          AND av.IsActive='Y') AS ValueCount
                               FROM M_Attribute a
                               INNER JOIN M_AttributeUse au ON (au.M_Attribute_ID=a.M_Attribute_ID
                                                                AND au.IsActive='Y')
                               WHERE au.M_AttributeSet_ID=@M_AttributeSet_ID
                                 AND a.IsActive='Y'";
            attrSql = MRole.GetDefault(ctx).AddAccessSQL(
                attrSql, "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            // ORDER BY goes after the access filter — AddAccessSQL appends its
            // predicate to the end of the statement it is handed.
            attrSql += " ORDER BY a.Name";

            DataSet dsa = Query(attrSql,
                new SqlParameter[] { new SqlParameter("@M_AttributeSet_ID", M_AttributeSet_ID) },
                "LoadAttributes(instance)");
            if (dsa != null && dsa.Tables.Count > 0)
            {
                foreach (DataRow ar in dsa.Tables[0].Rows)
                {
                    rows.Add(new AttributeRowData
                    {
                        Name        = Util.GetValueOfString(ar["AttributeName"]),
                        Kind        = "instance",
                        ValueType   = Util.GetValueOfString(ar["AttributeValueType"]),
                        ValueCount  = Util.GetValueOfInt(ar["ValueCount"]),
                        ChipKey     = Util.GetValueOfString(ar["IsMandatory"]) == "Y"
                                        ? "MANDATORY" : "OPTIONAL"
                    });
                }
            }

            // The set name rides on every row's owner field so the panel can put
            // it in the section summary without a second payload member.
            foreach (AttributeRowData row in rows) row.AttributeSetName = setName;
            return rows;
        }

        // ----------------------------------------------------------------- //
        //  3. Tax information                                                //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The product's tax category and its HSN / SAC classification code.
        /// Exactly the two cells the panel shows — no purchase tax, no reverse
        /// charge, no posting accounts, and no rate arithmetic.
        /// </summary>
        private TaxData LoadTax(Ctx ctx, int M_Product_ID)
        {
            bool hasHsn = ColumnExists("M_Product", "VAS_HSN_SACCode");
            string hsnExpr = hasHsn ? "p.VAS_HSN_SACCode" : "CAST(NULL AS VARCHAR(60))";

            string sql = @"SELECT tc.Name AS TaxCategoryName,
                                  " + hsnExpr + @" AS HsnSacCode
                           FROM M_Product p
                           LEFT OUTER JOIN C_TaxCategory tc ON (tc.C_TaxCategory_ID=p.C_TaxCategory_ID
                                                                AND tc.IsActive='Y')
                           WHERE p.M_Product_ID=@M_Product_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadTax");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;

            DataRow r = ds.Tables[0].Rows[0];
            TaxData t = new TaxData();
            t.TaxCategoryName = Util.GetValueOfString(r["TaxCategoryName"]);
            t.HsnSacCode      = Util.GetValueOfString(r["HsnSacCode"]);
            return t;
        }

        // ----------------------------------------------------------------- //
        //  4. Stock and availability (Item only)                             //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Headline inventory position: on hand from storage, reserved from
        /// COMPLETED sales orders, on order from COMPLETED purchase orders, and
        /// available-to-promise as on hand less reserved (never less on order).
        ///
        /// Three separate statements rather than one CTE. Each has its own main
        /// physical table and therefore its own access filter, which is exactly
        /// what the CTE rule asks for — and it keeps every bind name occurring
        /// once per statement, which positional binding requires.
        /// </summary>
        private StockSummaryData LoadStockSummary(Ctx ctx, int M_Product_ID)
        {
            StockSummaryData s = new StockSummaryData();

            // --- On hand, plus how widely it is spread ---
            string onHandSql = @"SELECT COALESCE(SUM(COALESCE(st.QtyOnHand, 0)), 0) AS OnHandQty,
                                        COUNT(DISTINCT loc.M_Warehouse_ID) AS WarehouseCount,
                                        COUNT(DISTINCT st.M_Locator_ID) AS LocatorCount
                                 FROM M_Storage st
                                 INNER JOIN M_Locator loc ON (loc.M_Locator_ID=st.M_Locator_ID
                                                              AND loc.IsActive='Y')
                                 WHERE st.M_Product_ID=@M_Product_ID
                                   AND st.IsActive='Y'";
            onHandSql = MRole.GetDefault(ctx).AddAccessSQL(
                onHandSql, "st", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(onHandSql, ProductParam(M_Product_ID), "LoadStockSummary(onHand)");
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                s.OnHandQty      = Util.GetValueOfDecimal(r["OnHandQty"]);
                s.WarehouseCount = Util.GetValueOfInt(r["WarehouseCount"]);
                s.LocatorCount   = Util.GetValueOfInt(r["LocatorCount"]);
            }

            // --- Reserved: outstanding quantity on completed SALES orders ---
            int reservedOrderCount;
            s.ReservedQty        = LoadOutstandingOrderQty(ctx, M_Product_ID, true,
                                                           out reservedOrderCount);
            s.ReservedOrderCount = reservedOrderCount;

            // --- On order: outstanding quantity on completed PURCHASE orders ---
            int ignoredCount;
            s.OnOrderQty = LoadOutstandingOrderQty(ctx, M_Product_ID, false, out ignoredCount);

            // The one availability formula this panel uses.
            s.AvailableToPromise = s.OnHandQty - s.ReservedQty;
            return s;
        }

        /// <summary>
        /// Outstanding (ordered less delivered) quantity for the product across
        /// COMPLETED orders of one direction, with the number of such orders.
        /// Only DocStatus 'CO' contributes — drafted, in-progress, closed, voided
        /// and reversed documents reserve nothing and order nothing. Return
        /// transactions are excluded.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="M_Product_ID">Selected product id.</param>
        /// <param name="isSalesOrder">True for reserved (SO), false for on order (PO).</param>
        /// <param name="orderCount">Receives the number of contributing orders.</param>
        /// <returns>The outstanding quantity; 0 when nothing is open.</returns>
        private decimal LoadOutstandingOrderQty(Ctx ctx, int M_Product_ID,
                                                bool isSalesOrder, out int orderCount)
        {
            orderCount = 0;
            // The flag is a stored column value, so it is compared as a plain
            // literal — never with the national-character prefix.
            string soTrx = isSalesOrder ? "Y" : "N";

            string sql = @"SELECT COALESCE(SUM(COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)), 0) AS OutstandingQty,
                                  COUNT(DISTINCT o.C_Order_ID) AS OrderCount
                           FROM C_Order o
                           INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID
                                                         AND ol.IsActive='Y')
                           WHERE ol.M_Product_ID=@M_Product_ID
                             AND o.IsActive='Y'
                             AND o.IsSOTrx='" + soTrx + @"'
                             AND COALESCE(o.IsReturnTrx, 'N')='N'
                             AND o.DocStatus='CO'
                             AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(sql, ProductParam(M_Product_ID),
                               "LoadOutstandingOrderQty(" + soTrx + ")");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return 0;

            DataRow r = ds.Tables[0].Rows[0];
            orderCount = Util.GetValueOfInt(r["OrderCount"]);
            return Util.GetValueOfDecimal(r["OutstandingQty"]);
        }

        // ----------------------------------------------------------------- //
        //  5. Stock details (Item only)                                      //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Where the stock physically sits: one row per warehouse, locator and
        /// attribute-set instance with a non-zero on-hand quantity. The attribute
        /// description is read from the instance record and refined in managed
        /// code; it is never concatenated in SQL, which would need a
        /// database-specific aggregate.
        /// </summary>
        private List<StockRowData> LoadStockDetails(Ctx ctx, int M_Product_ID)
        {
            List<StockRowData> rows = new List<StockRowData>();

            string sql = @"SELECT wh.M_Warehouse_ID,
                                  wh.Name AS WarehouseName,
                                  loc.M_Locator_ID,
                                  COALESCE(loc.LocatorCombination, loc.Value) AS LocatorName,
                                  st.M_AttributeSetInstance_ID,
                                  asi.Description AS AsiDescription,
                                  asi.Lot,
                                  asi.SerNo,
                                  asi.GuaranteeDate,
                                  SUM(COALESCE(st.QtyOnHand, 0)) AS QtyOnHand
                           FROM M_Storage st
                           INNER JOIN M_Locator loc ON (loc.M_Locator_ID=st.M_Locator_ID
                                                        AND loc.IsActive='Y')
                           INNER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID=loc.M_Warehouse_ID
                                                         AND wh.IsActive='Y')
                           LEFT OUTER JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID=st.M_AttributeSetInstance_ID
                                                                          AND st.M_AttributeSetInstance_ID > 0)
                           WHERE st.M_Product_ID=@M_Product_ID
                             AND st.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "st", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            // GROUP BY / HAVING / ORDER BY are appended after the access filter.
            sql += @" GROUP BY wh.M_Warehouse_ID, wh.Name, loc.M_Locator_ID,
                               COALESCE(loc.LocatorCombination, loc.Value),
                               st.M_AttributeSetInstance_ID, asi.Description,
                               asi.Lot, asi.SerNo, asi.GuaranteeDate
                      HAVING SUM(COALESCE(st.QtyOnHand, 0)) <> 0
                      ORDER BY wh.Name, 4, st.M_AttributeSetInstance_ID";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadStockDetails");
            if (ds == null || ds.Tables.Count == 0) return rows;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                StockRowData row = new StockRowData();
                row.M_Warehouse_ID = Util.GetValueOfInt(r["M_Warehouse_ID"]);
                row.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);
                row.M_Locator_ID   = Util.GetValueOfInt(r["M_Locator_ID"]);
                row.LocatorName    = Util.GetValueOfString(r["LocatorName"]);
                row.QtyOnHand      = Util.GetValueOfDecimal(r["QtyOnHand"]);
                row.M_AttributeSetInstance_ID = Util.GetValueOfInt(r["M_AttributeSetInstance_ID"]);
                row.Attributes     = BuildAsiText(
                    Util.GetValueOfString(r["AsiDescription"]),
                    Util.GetValueOfString(r["Lot"]),
                    Util.GetValueOfString(r["SerNo"]),
                    Util.GetValueOfDateTime(r["GuaranteeDate"]));
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Readable text for one attribute-set instance. The stored description is
        /// preferred — it is what the platform's own formatter produced — and the
        /// lot / serial / guarantee values stand in when there is none. A "--"
        /// placeholder description is treated as absent.
        /// </summary>
        private string BuildAsiText(string description, string lot, string serNo, DateTime? guarantee)
        {
            string desc = (description ?? "").Trim();
            if (desc.Length > 0 && desc != "--" && desc != "-") return desc;

            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(lot))   parts.Add(lot.Trim());
            if (!string.IsNullOrEmpty(serNo)) parts.Add(serNo.Trim());
            if (guarantee.HasValue)           parts.Add(guarantee.Value.ToString("yyyy-MM-dd"));
            return string.Join(" · ", parts.ToArray());
        }

        // ----------------------------------------------------------------- //
        //  6. UOM conversions                                                //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The alternate units the product can be transacted in. Product-specific
        /// conversion rows are preferred; a generic conversion for the same unit
        /// pair is only used when the product defines none, which is the rule the
        /// platform's own conversion helper follows.
        ///
        /// The rate is returned raw. The panel renders "= rate BaseUom"; no
        /// display string is assembled in SQL.
        /// </summary>
        private List<UomConversionData> LoadUomConversions(Ctx ctx, int M_Product_ID, string baseUomName)
        {
            List<UomConversionData> rows = new List<UomConversionData>();
            if (!TableExists("C_UOM_Conversion")) return rows;

            // Conversions FROM the product's base unit TO another unit. The
            // divide rate says how many base units one converted unit is worth,
            // which is the direction the panel reads ("Box-10 = 10 Each").
            string sql = @"SELECT conv.C_UOM_Conversion_ID,
                                  conv.M_Product_ID,
                                  conv.C_UOM_To_ID,
                                  uto.Name AS ConversionUomName,
                                  COALESCE(uto.StdPrecision, 0) AS UomPrecision,
                                  COALESCE(conv.DivideRate, 0) AS DivideRate,
                                  COALESCE(conv.MultiplyRate, 0) AS MultiplyRate
                           FROM C_UOM_Conversion conv
                           INNER JOIN M_Product p ON (p.C_UOM_ID=conv.C_UOM_ID)
                           INNER JOIN C_UOM uto ON (uto.C_UOM_ID=conv.C_UOM_To_ID
                                                    AND uto.IsActive='Y')
                           WHERE p.M_Product_ID=@M_Product_ID
                             AND conv.IsActive='Y'
                             AND (conv.M_Product_ID=p.M_Product_ID OR conv.M_Product_ID IS NULL)";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "conv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            // Product-specific rows first, so the de-duplication below keeps them.
            sql += " ORDER BY conv.C_UOM_To_ID, conv.M_Product_ID DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadUomConversions");
            if (ds == null || ds.Tables.Count == 0) return rows;

            // One row per target unit: the product's own conversion wins over the
            // generic one for the same pair. Done here rather than in SQL, which
            // would need a window function over a filter the access rewriter has
            // already appended to.
            List<int> seenUom = new List<int>();
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int toUom = Util.GetValueOfInt(r["C_UOM_To_ID"]);
                if (seenUom.Contains(toUom)) continue;
                seenUom.Add(toUom);

                UomConversionData c = new UomConversionData();
                c.C_UOM_To_ID   = toUom;
                c.UomName       = Util.GetValueOfString(r["ConversionUomName"]);
                c.UomPrecision  = Util.GetValueOfInt(r["UomPrecision"]);
                c.DivideRate    = Util.GetValueOfDecimal(r["DivideRate"]);
                c.MultiplyRate  = Util.GetValueOfDecimal(r["MultiplyRate"]);
                c.BaseUomName   = baseUomName;
                c.IsProductSpecific = Util.GetValueOfInt(r["M_Product_ID"]) > 0;

                // How many BASE units one converted unit is worth. The divide rate
                // states it directly; where only a multiply rate is stored it is
                // its reciprocal.
                if (c.DivideRate != 0)        c.RateToBase = c.DivideRate;
                else if (c.MultiplyRate != 0) c.RateToBase = 1m / c.MultiplyRate;
                else                          c.RateToBase = 0;

                if (c.RateToBase != 0) rows.Add(c);
            }
            return rows;
        }

        // ----------------------------------------------------------------- //
        //  7. Pricing                                                        //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// One row per price list on which the product actually has a price,
        /// reading the list's currently applicable version — the latest version
        /// whose ValidFrom has been reached. Cost is deliberately not read: this
        /// section shows selling / buying prices, and M_Cost is never queried.
        ///
        /// The ranking is a ROW_NUMBER over an access-filtered inner statement.
        /// The access filter is applied to that inner statement, on M_PriceList,
        /// before it is wrapped — a derived table is not a physical table and
        /// cannot carry one.
        /// </summary>
        private List<PricingRowData> LoadPricing(Ctx ctx, int M_Product_ID)
        {
            List<PricingRowData> rows = new List<PricingRowData>();

            // The business date the version is judged against comes from the
            // application context, never from the database server's clock.
            DateTime businessDate = GetBusinessDate(ctx);

            string inner = @"SELECT pl.M_PriceList_ID,
                                    pl.Name AS PriceListName,
                                    plv.M_PriceList_Version_ID,
                                    plv.Name AS VersionName,
                                    plv.ValidFrom,
                                    cur.ISO_Code,
                                    cur.CurSymbol,
                                    COALESCE(cur.StdPrecision, 2) AS CurPrecision
                             FROM M_PriceList pl
                             INNER JOIN M_PriceList_Version plv ON (plv.M_PriceList_ID=pl.M_PriceList_ID
                                                                    AND plv.IsActive='Y')
                             INNER JOIN C_Currency cur ON (cur.C_Currency_ID=pl.C_Currency_ID)
                             WHERE pl.IsActive='Y'
                               AND plv.ValidFrom <= @ValidFrom";
            inner = MRole.GetDefault(ctx).AddAccessSQL(
                inner, "pl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Ranked outside the access-filtered statement, then joined to the
            // product's own price rows. Two bind names, each occurring once, in
            // the order they appear: @ValidFrom then @M_Product_ID.
            string sql = @"SELECT rv.M_PriceList_ID,
                                  rv.PriceListName,
                                  rv.M_PriceList_Version_ID,
                                  rv.VersionName,
                                  rv.ValidFrom,
                                  rv.ISO_Code,
                                  rv.CurSymbol,
                                  rv.CurPrecision,
                                  COALESCE(pp.PriceList, 0) AS PriceList,
                                  COALESCE(pp.PriceStd, 0) AS PriceStd,
                                  COALESCE(pp.PriceLimit, 0) AS PriceLimit
                           FROM (SELECT v.M_PriceList_ID,
                                        v.PriceListName,
                                        v.M_PriceList_Version_ID,
                                        v.VersionName,
                                        v.ValidFrom,
                                        v.ISO_Code,
                                        v.CurSymbol,
                                        v.CurPrecision,
                                        ROW_NUMBER() OVER (PARTITION BY v.M_PriceList_ID
                                                           ORDER BY v.ValidFrom DESC,
                                                                    v.M_PriceList_Version_ID DESC) AS Rn
                                 FROM (" + inner + @") v) rv
                           INNER JOIN M_ProductPrice pp ON (pp.M_PriceList_Version_ID=rv.M_PriceList_Version_ID
                                                            AND pp.IsActive='Y')
                           WHERE rv.Rn=1
                             AND pp.M_Product_ID=@M_Product_ID
                           ORDER BY rv.PriceListName";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@ValidFrom", businessDate),
                new SqlParameter("@M_Product_ID", M_Product_ID)
            };

            DataSet ds = Query(sql, param, "LoadPricing");
            if (ds == null || ds.Tables.Count == 0) return rows;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new PricingRowData
                {
                    M_PriceList_ID = Util.GetValueOfInt(r["M_PriceList_ID"]),
                    PriceListName  = Util.GetValueOfString(r["PriceListName"]),
                    VersionName    = Util.GetValueOfString(r["VersionName"]),
                    ValidFrom      = Util.GetValueOfDateTime(r["ValidFrom"]),
                    ISO_Code       = Util.GetValueOfString(r["ISO_Code"]),
                    CurSymbol      = Util.GetValueOfString(r["CurSymbol"]),
                    CurPrecision   = Util.GetValueOfInt(r["CurPrecision"]),
                    PriceList      = Util.GetValueOfDecimal(r["PriceList"]),
                    PriceStd       = Util.GetValueOfDecimal(r["PriceStd"]),
                    PriceLimit     = Util.GetValueOfDecimal(r["PriceLimit"])
                });
            }
            return rows;
        }

        // ----------------------------------------------------------------- //
        //  8. Manufacturing - BOMs (Item only)                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The product's own BOMs and the BOMs it is consumed in, merged. No
        /// default flag, no version and no version date are read — those were
        /// removed from the specification and are not queried at all.
        /// </summary>
        private List<BomRowData> LoadBoms(Ctx ctx, int M_Product_ID)
        {
            List<BomRowData> rows = new List<BomRowData>();
            if (!TableExists("M_BOM")) return rows;

            LoadOwnBoms(ctx, M_Product_ID, rows);
            LoadWhereUsedBoms(ctx, M_Product_ID, rows);
            return rows;
        }

        /// <summary>The BOMs this product is the output of, with component counts.</summary>
        private void LoadOwnBoms(Ctx ctx, int M_Product_ID, List<BomRowData> rows)
        {
            // The component count is a correlated scalar subquery so a BOM is
            // listed once however many components it has.
            string sql = @"SELECT b.M_BOM_ID,
                                  b.Name AS BomName,
                                  b.Description AS BomDescription,
                                  COALESCE(p.IsVerified, 'N') AS IsVerified,
                                  (SELECT COUNT(1) FROM M_BOMProduct bp
                                    WHERE bp.M_BOM_ID=b.M_BOM_ID
                                      AND bp.IsActive='Y') AS ComponentCount
                           FROM M_BOM b
                           INNER JOIN M_Product p ON (p.M_Product_ID=b.M_Product_ID)
                           WHERE b.M_Product_ID=@M_Product_ID
                             AND b.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "b", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY b.Name";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadOwnBoms");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new BomRowData
                {
                    Kind           = "own",
                    M_BOM_ID       = Util.GetValueOfInt(r["M_BOM_ID"]),
                    Name           = Util.GetValueOfString(r["BomName"]),
                    Description    = Util.GetValueOfString(r["BomDescription"]),
                    ComponentCount = Util.GetValueOfInt(r["ComponentCount"]),
                    IsVerified     = Util.GetValueOfString(r["IsVerified"]) == "Y"
                });
            }
        }

        /// <summary>The BOMs that consume this product as a component.</summary>
        private void LoadWhereUsedBoms(Ctx ctx, int M_Product_ID, List<BomRowData> rows)
        {
            string sql = @"SELECT b.M_BOM_ID,
                                  b.Name AS BomName,
                                  parent.M_Product_ID AS ParentProductId,
                                  parent.Name AS ParentProductName,
                                  COALESCE(parent.IsVerified, 'N') AS IsVerified,
                                  COALESCE(bp.BOMQty, 0) AS BomQty
                           FROM M_BOMProduct bp
                           INNER JOIN M_BOM b ON (b.M_BOM_ID=bp.M_BOM_ID
                                                  AND b.IsActive='Y')
                           INNER JOIN M_Product parent ON (parent.M_Product_ID=b.M_Product_ID)
                           WHERE bp.M_ProductBOM_ID=@M_Product_ID
                             AND bp.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY parent.Name";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadWhereUsedBoms");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new BomRowData
                {
                    Kind             = "usedin",
                    M_BOM_ID         = Util.GetValueOfInt(r["M_BOM_ID"]),
                    Name             = Util.GetValueOfString(r["ParentProductName"]),
                    ParentProductId  = Util.GetValueOfInt(r["ParentProductId"]),
                    QtyPerParent     = Util.GetValueOfDecimal(r["BomQty"]),
                    IsVerified       = Util.GetValueOfString(r["IsVerified"]) == "Y"
                });
            }
        }

        // ----------------------------------------------------------------- //
        //  9. Quality parameters (Item only) - SPECIFICATION ONLY            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The quality parameters CONFIGURED against the product's quality plan.
        /// This section answers "what will be checked", never "what was found":
        /// no inspection record, document, inspector, date, lot or reading is
        /// queried, and no pass / fail / pending verdict is derived.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="VA010_QualityPlan_ID">The product's quality plan.</param>
        /// <returns>Configured parameters, or an empty list when VA010 is absent.</returns>
        private List<QualityParamRowData> LoadQualityParams(Ctx ctx, int VA010_QualityPlan_ID)
        {
            List<QualityParamRowData> rows = new List<QualityParamRowData>();
            if (VA010_QualityPlan_ID <= 0) return rows;
            if (!TableExists("VA010_QualityPlan") || !TableExists("VA010_AssgndParameters"))
                return rows;

            // The assigned-parameter columns differ between VA010 revisions, so
            // each optional one is probed and replaced by a typed NULL when the
            // schema does not have it.
            string minExpr    = QualityColumn("MinValue", "CAST(NULL AS NUMERIC)");
            string maxExpr    = QualityColumn("MaxValue", "CAST(NULL AS NUMERIC)");
            string obsExpr    = QualityColumn("VA010_Observation", "CAST(NULL AS VARCHAR(255))");
            string typeExpr   = QualityColumn("VA010_ParameterType", "CAST(NULL AS VARCHAR(60))");
            string weightExpr = QualityColumn("VA010_WeightagePercentage", "CAST(NULL AS NUMERIC)");
            string lineExpr   = QualityColumn("LineNo", "0");

            // The display column of a test parameter / list value also moves
            // between revisions — the same probe the QA widgets use.
            string paramNameCol = FindDisplayColumn("VA010_TestParameter",
                new string[] { "VA010_TestPrmtrName", "Name", "Description", "Value" });
            string valueNameCol = FindDisplayColumn("VA010_TestPrmtrList",
                new string[] { "VA010_ParameterValue", "Name", "Description", "Value" });

            string paramNameExpr = string.IsNullOrEmpty(paramNameCol)
                ? "CAST(NULL AS VARCHAR(255))" : "tp." + paramNameCol;
            string valueNameExpr = string.IsNullOrEmpty(valueNameCol)
                ? "CAST(NULL AS VARCHAR(255))" : "tpl." + valueNameCol;

            string sql = @"SELECT qp.VA010_QualityPlan_ID,
                                  qp.VA010_PlanName AS PlanName,
                                  qp.Description AS PlanDescription,
                                  ap.VA010_AssgndParameters_ID,
                                  " + lineExpr + @" AS LineNo,
                                  " + paramNameExpr + @" AS ParameterName,
                                  tp.Description AS ParameterDescription,
                                  " + minExpr + @" AS MinValue,
                                  " + maxExpr + @" AS MaxValue,
                                  " + obsExpr + @" AS Observation,
                                  " + typeExpr + @" AS ParameterType,
                                  " + weightExpr + @" AS Weightage,
                                  " + valueNameExpr + @" AS ListValue,
                                  ap.Description AS AssignedDescription
                           FROM VA010_QualityPlan qp
                           INNER JOIN VA010_AssgndParameters ap ON (ap.VA010_QualityPlan_ID=qp.VA010_QualityPlan_ID
                                                                    AND ap.IsActive='Y')
                           LEFT OUTER JOIN VA010_TestParameter tp ON (tp.VA010_TestParameter_ID=ap.VA010_TestParameter_ID
                                                                      AND tp.IsActive='Y')
                           LEFT OUTER JOIN VA010_TestPrmtrList tpl ON (tpl.VA010_TestPrmtrList_ID=ap.VA010_TestPrmtrList_ID
                                                                       AND tpl.IsActive='Y')
                           WHERE qp.VA010_QualityPlan_ID=@VA010_QualityPlan_ID
                             AND qp.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "qp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY 5, 6";

            DataSet ds = Query(sql,
                new SqlParameter[] { new SqlParameter("@VA010_QualityPlan_ID", VA010_QualityPlan_ID) },
                "LoadQualityParams");
            if (ds == null || ds.Tables.Count == 0) return rows;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                QualityParamRowData q = new QualityParamRowData();
                q.PlanName             = Util.GetValueOfString(r["PlanName"]);
                q.PlanDescription      = Util.GetValueOfString(r["PlanDescription"]);
                q.LineNo               = Util.GetValueOfInt(r["LineNo"]);
                q.ParameterName        = Util.GetValueOfString(r["ParameterName"]);
                q.ParameterDescription = Util.GetValueOfString(r["ParameterDescription"]);
                q.Observation          = Util.GetValueOfString(r["Observation"]);
                q.ParameterType        = Util.GetValueOfString(r["ParameterType"]);
                q.ListValue            = Util.GetValueOfString(r["ListValue"]);
                q.AssignedDescription  = Util.GetValueOfString(r["AssignedDescription"]);
                // A missing range bound is left null so the panel can tell
                // "no minimum configured" from "a minimum of zero".
                q.MinValue  = NullableDecimal(r["MinValue"]);
                q.MaxValue  = NullableDecimal(r["MaxValue"]);
                q.Weightage = NullableDecimal(r["Weightage"]);
                rows.Add(q);
            }
            return rows;
        }

        /// <summary>An assigned-parameter column when the schema has it, else a typed NULL.</summary>
        private string QualityColumn(string columnName, string fallbackExpr)
        {
            return ColumnExists("VA010_AssgndParameters", columnName)
                ? "ap." + columnName : fallbackExpr;
        }

        // ----------------------------------------------------------------- //
        //  10. Supplier information                                          //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The vendors the product is bought from, preferred vendor first, with
        /// the terms the vendor-product record actually stores — catalogue
        /// number, promised lead time and the last purchase price / date. No
        /// purchase-history query is run to recreate figures the row already
        /// carries, and no contract-expiry semantics are invented.
        /// </summary>
        private List<SupplierRowData> LoadSuppliers(Ctx ctx, int M_Product_ID)
        {
            List<SupplierRowData> rows = new List<SupplierRowData>();

            bool hasPriceLastPO   = ColumnExists("M_Product_PO", "PriceLastPO");
            bool hasDateLastPO    = ColumnExists("M_Product_PO", "PriceLastPODate");
            bool hasDeliveryTime  = ColumnExists("M_Product_PO", "DeliveryTime_Promised");
            bool hasVendorProduct = ColumnExists("M_Product_PO", "VendorProductNo");

            string priceLastExpr  = hasPriceLastPO
                ? "COALESCE(po.PriceLastPO, 0)" : "CAST(NULL AS NUMERIC)";
            string dateLastExpr   = hasDateLastPO
                ? "po.PriceLastPODate" : "CAST(NULL AS DATE)";
            string deliveryExpr   = hasDeliveryTime
                ? "COALESCE(po.DeliveryTime_Promised, 0)" : "0";
            string vendorProdExpr = hasVendorProduct
                ? "po.VendorProductNo" : "CAST(NULL AS VARCHAR(60))";

            string sql = @"SELECT po.M_Product_PO_ID,
                                  bp.C_BPartner_ID,
                                  bp.Name AS VendorName,
                                  COALESCE(po.IsCurrentVendor, 'N') AS IsCurrentVendor,
                                  " + vendorProdExpr + @" AS VendorProductNo,
                                  " + deliveryExpr + @" AS DeliveryTimePromised,
                                  " + priceLastExpr + @" AS PriceLastPO,
                                  " + dateLastExpr + @" AS PriceLastPODate,
                                  cur.ISO_Code,
                                  cur.CurSymbol,
                                  COALESCE(cur.StdPrecision, 2) AS CurPrecision
                           FROM M_Product_PO po
                           INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=po.C_BPartner_ID
                                                        AND bp.IsActive='Y'
                                                        AND bp.IsVendor='Y')
                           LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=po.C_Currency_ID)
                           WHERE po.M_Product_ID=@M_Product_ID
                             AND po.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "po", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY COALESCE(po.IsCurrentVendor, 'N') DESC, bp.Name";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadSuppliers");
            if (ds == null || ds.Tables.Count == 0) return rows;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new SupplierRowData
                {
                    C_BPartner_ID        = Util.GetValueOfInt(r["C_BPartner_ID"]),
                    VendorName           = Util.GetValueOfString(r["VendorName"]),
                    IsCurrentVendor      = Util.GetValueOfString(r["IsCurrentVendor"]) == "Y",
                    VendorProductNo      = Util.GetValueOfString(r["VendorProductNo"]),
                    DeliveryTimePromised = Util.GetValueOfInt(r["DeliveryTimePromised"]),
                    PriceLastPO          = NullableDecimal(r["PriceLastPO"]),
                    PriceLastPODate      = Util.GetValueOfDateTime(r["PriceLastPODate"]),
                    ISO_Code             = Util.GetValueOfString(r["ISO_Code"]),
                    CurSymbol            = Util.GetValueOfString(r["CurSymbol"]),
                    CurPrecision         = Util.GetValueOfInt(r["CurPrecision"])
                });
            }
            return rows;
        }

        // ----------------------------------------------------------------- //
        //  11 / 12. Sales and purchase orders                                //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The latest orders of one direction that contain the product. This is
        /// HISTORY, so every DocStatus is shown and the status code is returned
        /// raw for the panel to label — the reservation and on-order figures are
        /// a separate calculation that counts completed documents only.
        ///
        /// A product appearing on several lines of one order is aggregated to a
        /// single displayed row.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="M_Product_ID">Selected product id.</param>
        /// <param name="isSalesOrder">True for sales orders, false for purchase orders.</param>
        /// <returns>Up to <see cref="MAX_ORDERS"/> rows, newest first.</returns>
        private List<OrderRowData> LoadOrders(Ctx ctx, int M_Product_ID, bool isSalesOrder)
        {
            List<OrderRowData> rows = new List<OrderRowData>();
            string soTrx = isSalesOrder ? "Y" : "N";

            string inner = @"SELECT o.C_Order_ID,
                                    o.DocumentNo,
                                    o.DocStatus,
                                    o.DateOrdered,
                                    bp.Name AS BPartnerName,
                                    SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty,
                                    SUM(COALESCE(ol.LineNetAmt, 0)) AS LineNetAmt,
                                    MIN(uom.Name) AS UomName,
                                    MIN(cur.CurSymbol) AS CurSymbol,
                                    MIN(cur.ISO_Code) AS ISO_Code,
                                    MIN(COALESCE(cur.StdPrecision, 2)) AS CurPrecision
                             FROM C_Order o
                             INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID
                                                           AND ol.IsActive='Y')
                             LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=o.C_BPartner_ID)
                             LEFT OUTER JOIN C_UOM uom ON (uom.C_UOM_ID=ol.C_UOM_ID)
                             LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=o.C_Currency_ID)
                             WHERE ol.M_Product_ID=@M_Product_ID
                               AND o.IsActive='Y'
                               AND o.IsSOTrx='" + soTrx + "'";
            inner = MRole.GetDefault(ctx).AddAccessSQL(
                inner, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            // The aggregation collapses a product that repeats down one order.
            inner += @" GROUP BY o.C_Order_ID, o.DocumentNo, o.DocStatus, o.DateOrdered, bp.Name";

            // Latest N, ranked outside the access-filtered statement.
            string sql = @"SELECT x.C_Order_ID,
                                  x.DocumentNo,
                                  x.DocStatus,
                                  x.DateOrdered,
                                  x.BPartnerName,
                                  x.Qty,
                                  x.LineNetAmt,
                                  x.UomName,
                                  x.CurSymbol,
                                  x.ISO_Code,
                                  x.CurPrecision
                           FROM (SELECT g.C_Order_ID,
                                        g.DocumentNo,
                                        g.DocStatus,
                                        g.DateOrdered,
                                        g.BPartnerName,
                                        g.Qty,
                                        g.LineNetAmt,
                                        g.UomName,
                                        g.CurSymbol,
                                        g.ISO_Code,
                                        g.CurPrecision,
                                        ROW_NUMBER() OVER (ORDER BY g.DateOrdered DESC,
                                                                    g.C_Order_ID DESC) AS Rn
                                 FROM (" + inner + @") g) x
                           WHERE x.Rn <= " + MAX_ORDERS + @"
                           ORDER BY x.Rn";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadOrders(" + soTrx + ")");
            if (ds == null || ds.Tables.Count == 0) return rows;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new OrderRowData
                {
                    C_Order_ID   = Util.GetValueOfInt(r["C_Order_ID"]),
                    DocumentNo   = Util.GetValueOfString(r["DocumentNo"]),
                    DocStatus    = Util.GetValueOfString(r["DocStatus"]),
                    DateOrdered  = Util.GetValueOfDateTime(r["DateOrdered"]),
                    BPartnerName = Util.GetValueOfString(r["BPartnerName"]),
                    Qty          = Util.GetValueOfDecimal(r["Qty"]),
                    LineNetAmt   = Util.GetValueOfDecimal(r["LineNetAmt"]),
                    UomName      = Util.GetValueOfString(r["UomName"]),
                    CurSymbol    = Util.GetValueOfString(r["CurSymbol"]),
                    ISO_Code     = Util.GetValueOfString(r["ISO_Code"]),
                    CurPrecision = Util.GetValueOfInt(r["CurPrecision"]),
                    IsSOTrx      = isSalesOrder
                });
            }
            return rows;
        }

        // ----------------------------------------------------------------- //
        //  13. Recent transactions (Item only)                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The product's recent physical movements, read from the platform's own
        /// material-transaction abstraction so shipments, receipts, inventory
        /// moves and internal use come from one place instead of four document
        /// tables stitched together.
        ///
        /// A unit price is only reported where the movement genuinely has one —
        /// the order line behind a shipment or receipt. A stock move has none,
        /// and no cost is computed to fill the column.
        /// </summary>
        private List<TransactionRowData> LoadTransactions(Ctx ctx, int M_Product_ID)
        {
            List<TransactionRowData> rows = new List<TransactionRowData>();
            if (!TableExists("M_Transaction")) return rows;

            // Each source document names its own document type, and the panel
            // shows that name in front of the number ("Material Receipt · 500123")
            // — the number alone does not say what the movement was. The type is
            // read from the document, never inferred from MovementType, so a
            // tenant that has renamed or added document types sees its own words.
            //
            // C_DocType_ID is guarded on the inventory and movement documents:
            // it is not on every revision of those tables.
            bool invHasDocType = ColumnExists("M_Inventory", "C_DocType_ID");
            bool mvHasDocType  = ColumnExists("M_Movement", "C_DocType_ID");

            string invDocTypeJoin = invHasDocType
                ? "LEFT OUTER JOIN C_DocType invdt ON (invdt.C_DocType_ID=inv.C_DocType_ID)" : "";
            string mvDocTypeJoin = mvHasDocType
                ? "LEFT OUTER JOIN C_DocType mvdt ON (mvdt.C_DocType_ID=mv.C_DocType_ID)" : "";

            // COALESCE across the three, in the order a transaction can carry
            // them. A movement with no document type at all falls through to
            // NULL and the panel shows the number on its own.
            StringBuilder docTypeName = new StringBuilder("COALESCE(iodt.Name");
            if (invHasDocType) docTypeName.Append(", invdt.Name");
            if (mvHasDocType)  docTypeName.Append(", mvdt.Name");
            docTypeName.Append(")");

            string inner = @"SELECT mt.M_Transaction_ID,
                                    mt.MovementType,
                                    mt.MovementDate,
                                    COALESCE(mt.MovementQty, 0) AS MovementQty,
                                    COALESCE(io.DocumentNo, inv.DocumentNo, mv.DocumentNo) AS DocumentNo,
                                    " + docTypeName + @" AS DocTypeName,
                                    /* Where the row navigates to: the document
                                       the movement was posted by. */
                                    CASE WHEN io.M_InOut_ID IS NOT NULL THEN 'M_InOut'
                                         WHEN inv.M_Inventory_ID IS NOT NULL THEN 'M_Inventory'
                                         WHEN mv.M_Movement_ID IS NOT NULL THEN 'M_Movement'
                                    END AS DocTableName,
                                    COALESCE(io.M_InOut_ID, inv.M_Inventory_ID, mv.M_Movement_ID) AS DocRecordId,
                                    ol.PriceActual AS UnitPrice,
                                    cur.CurSymbol,
                                    cur.ISO_Code,
                                    COALESCE(cur.StdPrecision, 2) AS CurPrecision
                             FROM M_Transaction mt
                             LEFT OUTER JOIN M_InOutLine iol ON (iol.M_InOutLine_ID=mt.M_InOutLine_ID)
                             LEFT OUTER JOIN M_InOut io ON (io.M_InOut_ID=iol.M_InOut_ID)
                             LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID=iol.C_OrderLine_ID)
                             LEFT OUTER JOIN C_Order o ON (o.C_Order_ID=ol.C_Order_ID)
                             LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=o.C_Currency_ID)
                             LEFT OUTER JOIN C_DocType iodt ON (iodt.C_DocType_ID=io.C_DocType_ID)
                             LEFT OUTER JOIN M_InventoryLine invl ON (invl.M_InventoryLine_ID=mt.M_InventoryLine_ID)
                             LEFT OUTER JOIN M_Inventory inv ON (inv.M_Inventory_ID=invl.M_Inventory_ID)
                             " + invDocTypeJoin + @"
                             LEFT OUTER JOIN M_MovementLine mvl ON (mvl.M_MovementLine_ID=mt.M_MovementLine_ID)
                             LEFT OUTER JOIN M_Movement mv ON (mv.M_Movement_ID=mvl.M_Movement_ID)
                             " + mvDocTypeJoin + @"
                             WHERE mt.M_Product_ID=@M_Product_ID";
            inner = MRole.GetDefault(ctx).AddAccessSQL(
                inner, "mt", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"SELECT x.M_Transaction_ID,
                                  x.MovementType,
                                  x.MovementDate,
                                  x.MovementQty,
                                  x.DocumentNo,
                                  x.DocTypeName,
                                  x.DocTableName,
                                  x.DocRecordId,
                                  x.UnitPrice,
                                  x.CurSymbol,
                                  x.ISO_Code,
                                  x.CurPrecision
                           FROM (SELECT t.M_Transaction_ID,
                                        t.MovementType,
                                        t.MovementDate,
                                        t.MovementQty,
                                        t.DocumentNo,
                                        t.DocTypeName,
                                        t.DocTableName,
                                        t.DocRecordId,
                                        t.UnitPrice,
                                        t.CurSymbol,
                                        t.ISO_Code,
                                        t.CurPrecision,
                                        ROW_NUMBER() OVER (ORDER BY t.MovementDate DESC,
                                                                    t.M_Transaction_ID DESC) AS Rn
                                 FROM (" + inner + @") t) x
                           WHERE x.Rn <= " + MAX_TRANSACTIONS + @"
                           ORDER BY x.Rn";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadTransactions");
            if (ds == null || ds.Tables.Count == 0) return rows;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new TransactionRowData
                {
                    M_Transaction_ID = Util.GetValueOfInt(r["M_Transaction_ID"]),
                    MovementType     = Util.GetValueOfString(r["MovementType"]),
                    MovementDate     = Util.GetValueOfDateTime(r["MovementDate"]),
                    MovementQty      = Util.GetValueOfDecimal(r["MovementQty"]),
                    DocumentNo       = Util.GetValueOfString(r["DocumentNo"]),
                    DocTypeName      = Util.GetValueOfString(r["DocTypeName"]),
                    DocTableName     = Util.GetValueOfString(r["DocTableName"]),
                    DocRecordId      = Util.GetValueOfInt(r["DocRecordId"]),
                    UnitPrice        = NullableDecimal(r["UnitPrice"]),
                    CurSymbol        = Util.GetValueOfString(r["CurSymbol"]),
                    ISO_Code         = Util.GetValueOfString(r["ISO_Code"]),
                    CurPrecision     = Util.GetValueOfInt(r["CurPrecision"])
                });
            }
            return rows;
        }

        // ----------------------------------------------------------------- //
        //  14. Accounting details                                            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The posting accounts the product resolves to under the client's
        /// primary accounting schema — the product's own defaults, falling back
        /// to its category's for any account the product does not override.
        ///
        /// Which accounts are reported depends on the product type, and a type
        /// with nothing to report gets no section at all:
        ///   Item     - asset, revenue, COGS, purchase price variance
        ///   Service  - NOTHING. No revenue, no deferred revenue, no expense.
        ///   Resource - the resource absorption account, when one is configured
        ///   Expense  - the product expense account, when one is configured
        /// </summary>
        private AccountingData LoadAccounting(Ctx ctx, int M_Product_ID,
                                              int M_Product_Category_ID, string productType)
        {
            // Service products are excluded outright — no account of any kind is
            // queried or shown for them.
            if (productType == TYPE_SERVICE) return null;

            // Which account columns this type is allowed to report.
            List<string> wanted = new List<string>();
            if (productType == TYPE_ITEM)
            {
                wanted.Add("P_Asset_Acct");
                wanted.Add("P_Revenue_Acct");
                wanted.Add("P_COGS_Acct");
                wanted.Add("P_PurchasePriceVariance_Acct");
            }
            else if (productType == TYPE_RESOURCE)
            {
                // Only when the column is actually present in this schema — the
                // absorption account is not on every deployment.
                if (ColumnExists("M_Product_Acct", "P_Resource_Absorption_Acct"))
                    wanted.Add("P_Resource_Absorption_Acct");
            }
            else if (productType == TYPE_EXPENSE)
            {
                wanted.Add("P_Expense_Acct");
            }
            if (wanted.Count == 0) return null;

            AcctSchemaInfo schema = GetPrimaryAcctSchema(ctx);
            if (schema == null || schema.C_AcctSchema_ID <= 0) return null;

            AccountingData acct = new AccountingData();
            acct.CostingMethod = schema.CostingMethod;
            acct.CurrencyISO   = schema.CurrencyISO;
            acct.CurSymbol     = schema.CurSymbol;
            acct.Rows          = new List<AccountRowData>();

            // The product's own defaults, then the category's for anything the
            // product leaves unset. Two small reads rather than one COALESCE
            // across a join, so "which level answered" stays visible.
            Dictionary<string, int> productAcct  = LoadAcctRow(ctx, "M_Product_Acct",
                "M_Product_ID", M_Product_ID, schema.C_AcctSchema_ID, wanted);
            Dictionary<string, int> categoryAcct = LoadAcctRow(ctx, "M_Product_Category_Acct",
                "M_Product_Category_ID", M_Product_Category_ID, schema.C_AcctSchema_ID, wanted);

            List<int> combinationIds = new List<int>();
            Dictionary<string, int> effective = new Dictionary<string, int>();
            foreach (string column in wanted)
            {
                int id = 0;
                if (productAcct.ContainsKey(column))  id = productAcct[column];
                if (id <= 0 && categoryAcct.ContainsKey(column)) id = categoryAcct[column];
                if (id <= 0) continue;
                effective[column] = id;
                if (!combinationIds.Contains(id)) combinationIds.Add(id);
            }
            if (effective.Count == 0) return null;   // nothing configured - no section

            Dictionary<int, ValidCombinationInfo> combos = LoadCombinations(ctx, combinationIds);

            // Reported in the order the type declares them, not in whatever order
            // the dictionary happens to return.
            foreach (string column in wanted)
            {
                if (!effective.ContainsKey(column)) continue;
                int id = effective[column];

                AccountRowData row = new AccountRowData();
                row.AccountRole = column;
                row.IsFromCategory = !(productAcct.ContainsKey(column) && productAcct[column] > 0);
                if (combos.ContainsKey(id))
                {
                    row.Combination = combos[id].Combination;
                    row.Description = combos[id].Description;
                }
                acct.Rows.Add(row);
            }
            return acct.Rows.Count > 0 ? acct : null;
        }

        /// <summary>
        /// Reads the requested account columns from one accounting-defaults table
        /// for one owner record and accounting schema.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="tableName">M_Product_Acct or M_Product_Category_Acct.</param>
        /// <param name="keyColumn">That table's owner FK column name.</param>
        /// <param name="keyValue">The owner record id; 0 skips the read.</param>
        /// <param name="C_AcctSchema_ID">Accounting schema to read under.</param>
        /// <param name="columns">Account columns to read.</param>
        /// <returns>Column name to C_ValidCombination_ID; empty when nothing is set.</returns>
        private Dictionary<string, int> LoadAcctRow(Ctx ctx, string tableName, string keyColumn,
                                                    int keyValue, int C_AcctSchema_ID,
                                                    List<string> columns)
        {
            Dictionary<string, int> found = new Dictionary<string, int>();
            if (keyValue <= 0 || columns.Count == 0) return found;

            // Only columns that exist are selected; the table name and column
            // names come from this class's own fixed list and the dictionary,
            // never from user input.
            List<string> available = new List<string>();
            StringBuilder select = new StringBuilder();
            foreach (string column in columns)
            {
                if (!ColumnExists(tableName, column)) continue;
                if (select.Length > 0) select.Append(", ");
                select.Append("acct.").Append(column);
                available.Add(column);
            }
            if (available.Count == 0) return found;

            // The schema id is inlined as an integer so the statement carries a
            // single bind name, which positional binding requires.
            string sql = "SELECT " + select
                       + " FROM " + tableName + @" acct
                          WHERE acct." + keyColumn + @"=@KeyValue
                            AND acct.C_AcctSchema_ID=" + C_AcctSchema_ID + @"
                            AND acct.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "acct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(sql,
                new SqlParameter[] { new SqlParameter("@KeyValue", keyValue) },
                "LoadAcctRow(" + tableName + ")");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return found;

            DataRow r = ds.Tables[0].Rows[0];
            foreach (string column in available)
            {
                int id = Util.GetValueOfInt(r[column]);
                if (id > 0) found[column] = id;
            }
            return found;
        }

        /// <summary>
        /// Resolves account combinations to their display value and description,
        /// using the same C_ValidCombination record the accounting screens read.
        /// One lookup for every combination in play.
        /// </summary>
        private Dictionary<int, ValidCombinationInfo> LoadCombinations(Ctx ctx, List<int> ids)
        {
            Dictionary<int, ValidCombinationInfo> map = new Dictionary<int, ValidCombinationInfo>();
            if (ids.Count == 0) return map;

            // The id list is built from integers this model read out of the
            // database, so nothing typed by a user reaches the statement.
            string sql = @"SELECT vc.C_ValidCombination_ID,
                                  vc.Combination,
                                  vc.Description
                           FROM C_ValidCombination vc
                           WHERE vc.C_ValidCombination_ID IN (" + JoinIds(ids) + @")
                             AND vc.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "vc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(sql, null, "LoadCombinations");
            if (ds == null || ds.Tables.Count == 0) return map;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                map[Util.GetValueOfInt(r["C_ValidCombination_ID"])] = new ValidCombinationInfo
                {
                    Combination = Util.GetValueOfString(r["Combination"]),
                    Description = Util.GetValueOfString(r["Description"])
                };
            }
            return map;
        }

        /// <summary>
        /// The client's primary accounting schema (AD_ClientInfo.C_AcctSchema1_ID)
        /// with its costing method and currency — the same route the platform
        /// uses to reach the tenant's base currency.
        /// </summary>
        private AcctSchemaInfo GetPrimaryAcctSchema(Ctx ctx)
        {
            string sql = @"SELECT acs.C_AcctSchema_ID,
                                  acs.CostingMethod,
                                  cur.ISO_Code,
                                  CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol
                                       ELSE cur.ISO_Code END AS CurrencySymbol,
                                  COALESCE(cur.StdPrecision, 2) AS StdPrecision
                           FROM AD_ClientInfo ci
                           INNER JOIN C_AcctSchema acs ON (acs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                           INNER JOIN C_Currency cur ON (cur.C_Currency_ID=acs.C_Currency_ID)
                           WHERE ci.AD_Client_ID=@AD_Client_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(sql,
                new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) },
                "GetPrimaryAcctSchema");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;

            DataRow r = ds.Tables[0].Rows[0];
            return new AcctSchemaInfo
            {
                C_AcctSchema_ID = Util.GetValueOfInt(r["C_AcctSchema_ID"]),
                CostingMethod   = Util.GetValueOfString(r["CostingMethod"]),
                CurrencyISO     = Util.GetValueOfString(r["ISO_Code"]),
                CurSymbol       = Util.GetValueOfString(r["CurrencySymbol"]),
                StdPrecision    = Util.GetValueOfInt(r["StdPrecision"])
            };
        }

        // ----------------------------------------------------------------- //
        //  15. Activity                                                      //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The product's unified timeline: field changes, workflow steps, mails,
        /// notes, tasks and appointments. Each source is a small independent
        /// query behind its own guard, converted to one common event shape and
        /// merged here — a single cross-source UNION would fail whole if any one
        /// optional table were absent.
        ///
        /// NONE of these queries carries MRole.AddAccessSQL, and that is
        /// deliberate. They are children of a product row that has ALREADY been
        /// read under the access filter (LoadSummary), and each is pinned to that
        /// one record by AD_Table_ID + Record_ID, so there is nothing further to
        /// authorise. Applying it here does not merely add nothing: these are
        /// audit / correspondence tables, and the SQL_FULLYQUALIFIED rewriter
        /// either appends a predicate no row satisfies or reaches for a column
        /// the statement has not got — which is why the timeline came back empty.
        /// The same rule is what VAS_092 and VAS_099 follow: the access filter
        /// goes on the document, never on its trail.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="M_Product_ID">Selected product id.</param>
        /// <returns>Events newest first, capped at <see cref="MAX_ACTIVITY"/>.</returns>
        private List<ActivityData> LoadActivity(Ctx ctx, int M_Product_ID)
        {
            List<ActivityData> events = new List<ActivityData>();
            if (_productTableId <= 0) return events;

            LoadFieldChangeActivity(ctx, M_Product_ID, events);
            LoadWorkflowActivity(ctx, M_Product_ID, events);
            LoadMailActivity(ctx, M_Product_ID, events);
            LoadNoteActivity(ctx, M_Product_ID, events);
            LoadChatActivity(ctx, M_Product_ID, events);
            LoadAppointmentActivity(ctx, M_Product_ID, events);

            // Newest first, on the raw timestamp. Formatting happens on the client.
            events.Sort(delegate (ActivityData a, ActivityData b)
            {
                return b.EventDate.GetValueOrDefault(DateTime.MinValue)
                        .CompareTo(a.EventDate.GetValueOrDefault(DateTime.MinValue));
            });
            if (events.Count > MAX_ACTIVITY) events = events.GetRange(0, MAX_ACTIVITY);
            return events;
        }

        /// <summary>Field-level changes logged against the product (AD_ChangeLog).</summary>
        private void LoadFieldChangeActivity(Ctx ctx, int M_Product_ID, List<ActivityData> list)
        {
            string sql = @"SELECT cl.AD_ChangeLog_ID,
                                  cl.OldValue,
                                  cl.NewValue,
                                  cl.Created,
                                  COALESCE(col.Name, col.ColumnName) AS FieldName,
                                  u.Name AS ActorName
                           FROM AD_ChangeLog cl
                           LEFT OUTER JOIN AD_Column col ON (col.AD_Column_ID=cl.AD_Column_ID)
                           LEFT OUTER JOIN AD_User u ON (u.AD_User_ID=cl.CreatedBy)
                           WHERE cl.AD_Table_ID=" + _productTableId + @"
                             AND cl.Record_ID=@M_Product_ID
                             AND COALESCE(cl.IsActive, 'Y')='Y'
                           ORDER BY cl.Created DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadFieldChangeActivity");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                list.Add(new ActivityData
                {
                    Id        = Util.GetValueOfInt(r["AD_ChangeLog_ID"]),
                    Type      = "fieldupdate",
                    Title     = Util.GetValueOfString(r["FieldName"]),
                    OldValue  = Util.GetValueOfString(r["OldValue"]),
                    NewValue  = Util.GetValueOfString(r["NewValue"]),
                    Actor     = Util.GetValueOfString(r["ActorName"]),
                    EventDate = Util.GetValueOfDateTime(r["Created"])
                });
            }
        }

        /// <summary>
        /// Completed workflow steps recorded against the product, read through the
        /// platform's own workflow-to-record link rather than inferred from audit
        /// rows. Only the node name, state, actor and moment are reported — this
        /// is a timeline entry, not a workflow viewer.
        /// </summary>
        private void LoadWorkflowActivity(Ctx ctx, int M_Product_ID, List<ActivityData> list)
        {
            string sql = @"SELECT wfa.AD_WF_Activity_ID,
                                  wfa.WFState,
                                  wfa.Created,
                                  wfn.Name AS NodeName,
                                  u.Name AS ActorName
                           FROM AD_WF_Activity wfa
                           INNER JOIN AD_WF_Process wfp ON (wfp.AD_WF_Process_ID=wfa.AD_WF_Process_ID)
                           LEFT OUTER JOIN AD_WF_Node wfn ON (wfn.AD_WF_Node_ID=wfa.AD_WF_Node_ID)
                           LEFT OUTER JOIN AD_User u ON (u.AD_User_ID=wfa.CreatedBy)
                           WHERE wfp.AD_Table_ID=" + _productTableId + @"
                             AND wfp.Record_ID=@M_Product_ID
                             AND wfa.IsActive='Y'
                             AND wfp.IsActive='Y'
                           ORDER BY wfa.Created DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadWorkflowActivity");
            if (ds == null || ds.Tables.Count == 0) return;

            // WFState is a LIST column, so its codes ('CC', 'OR', …) are stored
            // values, not labels. They are resolved against the dictionary in the
            // reader's own language rather than mapped to English on the client —
            // a coded field must never reach the screen raw.
            Dictionary<string, string> stateLabels =
                LoadRefListLabels(ctx, "AD_WF_Activity", "WFState");

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string stateCode = Util.GetValueOfString(r["WFState"]);
                string stateName = "";
                if (!string.IsNullOrEmpty(stateCode) && stateLabels.ContainsKey(stateCode))
                    stateName = stateLabels[stateCode];

                list.Add(new ActivityData
                {
                    Id        = Util.GetValueOfInt(r["AD_WF_Activity_ID"]),
                    Type      = "workflow",
                    Title     = Util.GetValueOfString(r["NodeName"]),
                    StateCode = stateCode,
                    StateName = stateName,
                    Actor     = Util.GetValueOfString(r["ActorName"]),
                    EventDate = Util.GetValueOfDateTime(r["Created"])
                });
            }
        }

        /// <summary>
        /// The display labels of a LIST column's reference values, keyed by their
        /// stored code, in the logged-in user's language. Falls back through the
        /// translated name, the base name and finally the raw code, so a value
        /// always has something readable against it.
        ///
        /// The reference is found from the column itself
        /// (AD_Column.AD_Reference_Value_ID), never assumed — the same column can
        /// carry a different list on another installation.
        /// </summary>
        /// <param name="ctx">User context (supplies the language).</param>
        /// <param name="tableName">Table the column belongs to.</param>
        /// <param name="columnName">The list column.</param>
        /// <returns>Code to label; empty when the column is not a list.</returns>
        private Dictionary<string, string> LoadRefListLabels(Ctx ctx, string tableName, string columnName)
        {
            Dictionary<string, string> labels = new Dictionary<string, string>();
            try
            {
                // Three binds, each occurring once, in the order they appear —
                // @AD_Language, then @TableName, then @ColumnName.
                string sql = @"SELECT rl.Value,
                                      COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                               FROM AD_Column c
                               INNER JOIN AD_Table t ON (t.AD_Table_ID=c.AD_Table_ID)
                               INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID=c.AD_Reference_Value_ID
                                                             AND rl.IsActive='Y')
                               LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID
                                                                       AND rlt.AD_Language=@AD_Language
                                                                       AND rlt.IsActive='Y')
                               WHERE UPPER(t.TableName)=UPPER(@TableName)
                                 AND UPPER(c.ColumnName)=UPPER(@ColumnName)
                                 AND c.IsActive='Y'";

                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@AD_Language", ctx.GetAD_Language()),
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                };

                DataSet ds = Query(sql, param, "LoadRefListLabels(" + tableName + "." + columnName + ")");
                if (ds == null || ds.Tables.Count == 0) return labels;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string code = Util.GetValueOfString(r["Value"]);
                    if (!string.IsNullOrEmpty(code) && !labels.ContainsKey(code))
                        labels[code] = Util.GetValueOfString(r["DisplayName"]);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: without labels the panel shows the code, which is
                // still better than showing nothing.
                _log.Severe("VAS_190 LoadRefListLabels(" + tableName + "." + columnName + "): " + ex.Message);
            }
            return labels;
        }

        /// <summary>
        /// Mails sent to or received against the product. The body travels with
        /// the row so the panel can reveal it inline without a second round trip;
        /// the attachment BLOB is never selected.
        /// </summary>
        private void LoadMailActivity(Ctx ctx, int M_Product_ID, List<ActivityData> list)
        {
            if (!TableExists("MailAttachment1")) return;

            string sql = @"SELECT ma.MailAttachment1_ID,
                                  ma.Title,
                                  ma.TextMsg,
                                  ma.MailAddress,
                                  ma.MailAddressFrom,
                                  ma.MailAddressCc,
                                  ma.MailAddressBcc,
                                  ma.IsMailSent,
                                  ma.DateMailReceived,
                                  ma.Created,
                                  u.Name AS ActorName
                           FROM MailAttachment1 ma
                           LEFT OUTER JOIN AD_User u ON (u.AD_User_ID=ma.CreatedBy)
                           WHERE ma.AD_Table_ID=" + _productTableId + @"
                             AND ma.Record_ID=@M_Product_ID
                             AND COALESCE(ma.IsActive, 'Y')='Y'
                           ORDER BY COALESCE(ma.DateMailReceived, ma.Created) DESC,
                                    ma.MailAttachment1_ID DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadMailActivity");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                DateTime? received = Util.GetValueOfDateTime(r["DateMailReceived"]);
                ActivityData a = new ActivityData();
                a.Id        = Util.GetValueOfInt(r["MailAttachment1_ID"]);
                a.Type      = "mail";
                a.Title     = Util.GetValueOfString(r["Title"]);
                // An HTML mail stores its markup here and the panel renders the
                // body as text, so it is flattened before it leaves the server.
                a.Body      = MailBodyToText(Util.GetValueOfString(r["TextMsg"]));
                a.MailTo    = Util.GetValueOfString(r["MailAddress"]);
                a.MailFrom  = Util.GetValueOfString(r["MailAddressFrom"]);
                a.MailCc    = Util.GetValueOfString(r["MailAddressCc"]);
                a.MailBcc   = Util.GetValueOfString(r["MailAddressBcc"]);
                a.IsSent    = Util.GetValueOfString(r["IsMailSent"]) == "Y";
                a.Actor     = Util.GetValueOfString(r["ActorName"]);
                a.EventDate = received.HasValue ? received : Util.GetValueOfDateTime(r["Created"]);
                list.Add(a);
            }
        }

        /// <summary>Notes logged against the product (AD_Note).</summary>
        private void LoadNoteActivity(Ctx ctx, int M_Product_ID, List<ActivityData> list)
        {
            if (!TableExists("AD_Note")) return;

            string sql = @"SELECT n.AD_Note_ID,
                                  n.Description,
                                  n.TextMsg,
                                  n.Reference,
                                  n.Created,
                                  u.Name AS ActorName
                           FROM AD_Note n
                           LEFT OUTER JOIN AD_User u ON (u.AD_User_ID=n.CreatedBy)
                           WHERE n.AD_Table_ID=" + _productTableId + @"
                             AND n.Record_ID=@M_Product_ID
                             AND n.IsActive='Y'
                           ORDER BY n.Created DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadNoteActivity");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string title = Util.GetValueOfString(r["Description"]);
                if (string.IsNullOrEmpty(title)) title = Util.GetValueOfString(r["Reference"]);

                list.Add(new ActivityData
                {
                    Id        = Util.GetValueOfInt(r["AD_Note_ID"]),
                    Type      = "note",
                    Title     = title,
                    Body      = Util.GetValueOfString(r["TextMsg"]),
                    Actor     = Util.GetValueOfString(r["ActorName"]),
                    EventDate = Util.GetValueOfDateTime(r["Created"])
                });
            }
        }

        /// <summary>
        /// Chat comments posted against the product (CM_Chat / CM_ChatEntry).
        /// These are a separate source from AD_Note: a note is a system-raised
        /// record, a chat entry is something a person typed on the record, and a
        /// timeline that carries one but not the other reads as if half the
        /// conversation never happened.
        ///
        /// The author is COALESCE(CM_ChatEntry.AD_User_ID, CreatedBy). An entry
        /// written through the platform's own chat plumbing leaves AD_User_ID
        /// null, which would print the comment with a timestamp and nobody's name
        /// against it — the same correction VAS_092 and VAS_099 carry.
        /// </summary>
        private void LoadChatActivity(Ctx ctx, int M_Product_ID, List<ActivityData> list)
        {
            if (!TableExists("CM_ChatEntry")) return;

            string sql = @"SELECT ce.CM_ChatEntry_ID,
                                  ce.CharacterData,
                                  ce.Created,
                                  COALESCE(u.Name, cu.Name) AS ActorName
                           FROM CM_ChatEntry ce
                           INNER JOIN CM_Chat ch ON (ch.CM_Chat_ID=ce.CM_Chat_ID)
                           LEFT OUTER JOIN AD_User u ON (u.AD_User_ID=ce.AD_User_ID)
                           LEFT OUTER JOIN AD_User cu ON (cu.AD_User_ID=ce.CreatedBy)
                           WHERE ch.AD_Table_ID=" + _productTableId + @"
                             AND ch.Record_ID=@M_Product_ID
                             AND ce.IsActive='Y'
                             AND ch.IsActive='Y'
                           ORDER BY ce.Created DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadChatActivity");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string text = Util.GetValueOfString(r["CharacterData"]);

                list.Add(new ActivityData
                {
                    Id        = Util.GetValueOfInt(r["CM_ChatEntry_ID"]),
                    Type      = "chat",
                    // The comment IS the entry — there is no separate subject, so
                    // the text heads the row and travels whole in the body for
                    // the panel to show without truncating on screen.
                    Title     = text,
                    Body      = text,
                    Actor     = Util.GetValueOfString(r["ActorName"]),
                    EventDate = Util.GetValueOfDateTime(r["Created"])
                });
            }
        }

        /// <summary>
        /// Tasks and appointments linked to the product (AppointmentsInfo). The
        /// IsTask flag is what separates the two; a task's open / completed state
        /// comes from IsClosed, and no label is invented from a numeric status.
        /// </summary>
        private void LoadAppointmentActivity(Ctx ctx, int M_Product_ID, List<ActivityData> list)
        {
            if (!TableExists("AppointmentsInfo")) return;

            // IsDeleted is not on every revision of the table.
            string deletedFilter = ColumnExists("AppointmentsInfo", "IsDeleted")
                ? " AND COALESCE(ai.IsDeleted, 'N')='N'" : "";

            string sql = @"SELECT ai.AppointmentsInfo_ID,
                                  ai.Subject,
                                  ai.Description,
                                  ai.StartDate,
                                  ai.EndDate,
                                  ai.Location,
                                  COALESCE(ai.IsTask, 'N') AS IsTask,
                                  COALESCE(ai.IsClosed, 'N') AS IsClosed,
                                  COALESCE(ai.IsCancelled, 'N') AS IsCancelled,
                                  ai.Created,
                                  u.Name AS ActorName
                           FROM AppointmentsInfo ai
                           LEFT OUTER JOIN AD_User u ON (u.AD_User_ID=ai.AD_User_ID)
                           WHERE ai.AD_Table_ID=" + _productTableId + @"
                             AND ai.Record_ID=@M_Product_ID
                             AND ai.IsActive='Y'" + deletedFilter + @"
                           ORDER BY COALESCE(ai.StartDate, ai.Created) DESC,
                                    ai.AppointmentsInfo_ID DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadAppointmentActivity");
            if (ds == null || ds.Tables.Count == 0) return;

            // AppointmentsInfo stores one row per ATTENDEE, not one per meeting,
            // so a meeting with five attendees would otherwise fill the timeline
            // with five identical entries. Collapsed on (StartDate, Subject) —
            // the rows arrive ordered by id, so the first one seen wins and the
            // entry stays stable between refreshes.
            List<string> seenMeetings = new List<string>();

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                bool isTask = Util.GetValueOfString(r["IsTask"]) == "Y";
                DateTime? start = Util.GetValueOfDateTime(r["StartDate"]);

                string meetingKey = (start.HasValue ? start.Value.ToString("s") : "")
                                  + "|" + Util.GetValueOfString(r["Subject"]);
                if (seenMeetings.Contains(meetingKey)) continue;
                seenMeetings.Add(meetingKey);

                list.Add(new ActivityData
                {
                    Id          = Util.GetValueOfInt(r["AppointmentsInfo_ID"]),
                    Type        = isTask ? "task" : "appointment",
                    Title       = Util.GetValueOfString(r["Subject"]),
                    Body        = Util.GetValueOfString(r["Description"]),
                    Location    = Util.GetValueOfString(r["Location"]),
                    IsClosed    = Util.GetValueOfString(r["IsClosed"]) == "Y",
                    IsCancelled = Util.GetValueOfString(r["IsCancelled"]) == "Y",
                    StartDate   = start,
                    EndDate     = Util.GetValueOfDateTime(r["EndDate"]),
                    Actor       = Util.GetValueOfString(r["ActorName"]),
                    EventDate   = start.HasValue ? start : Util.GetValueOfDateTime(r["Created"])
                });
            }
        }

        // ----------------------------------------------------------------- //
        //  Mail body flattening                                              //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Cheap "is this markup" test — a real tag, not a stray '&lt;' in a
        /// plain-text mail ("qty &lt; 10"), so a plain body is left untouched.
        /// </summary>
        private static readonly Regex HTML_BODY = new Regex(
            @"<\s*/?\s*(html|body|head|br|p|div|table|thead|tbody|tr|td|th|span|a|img|b|i|u"
            + @"|strong|em|ul|ol|li|h[1-6]|font|style|script)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Renders a mail body as readable plain text. A mail sent as HTML stores
        /// its markup in TextMsg and the panel shows the body as text — so
        /// without this the reader would get tags instead of a message. Block
        /// markup becomes line breaks, everything else is dropped and entities
        /// are decoded last, so what reaches the browser is text it can safely
        /// escape: no markup is ever handed to the panel.
        /// </summary>
        private static string MailBodyToText(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            if (!HTML_BODY.IsMatch(body)) return body;      // plain-text mail

            try
            {
                string s = body;
                s = Regex.Replace(s, @"<\s*(script|style|head)\b[^>]*>.*?<\s*/\s*\1\s*>", " ",
                                  RegexOptions.IgnoreCase | RegexOptions.Singleline);
                s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(p|div|tr|li|h[1-6]|table|blockquote)\s*>", "\n",
                                  RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*(p|div|li|h[1-6])\b[^>]*>", "\n", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(td|th)\s*>", "\t", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<[^>]*>", string.Empty);
                s = WebUtility.HtmlDecode(s);
                s = s.Replace(' ', ' ');
                s = s.Replace("\r\n", "\n").Replace('\r', '\n');
                s = Regex.Replace(s, @"[^\S\n\t]+", " ");
                s = Regex.Replace(s, @"[ \t]*\n[ \t]*", "\n");
                s = Regex.Replace(s, @"\n{3,}", "\n\n");
                return s.Trim();
            }
            catch (Exception ex)
            {
                // Never lose the mail over a formatting failure — show it raw.
                _log.Severe("VAS_190 MailBodyToText: " + ex.Message);
                return body;
            }
        }

        // ----------------------------------------------------------------- //
        //  Infrastructure helpers                                            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Runs one section query behind its own guard. A section that fails is
        /// logged and comes back empty; it never takes the rest of the panel down
        /// with it.
        /// </summary>
        /// <param name="sql">The statement to run.</param>
        /// <param name="param">Bind parameters, or null when the statement has none.</param>
        /// <param name="where">Caller name, for the log line.</param>
        /// <returns>The dataset, or null when the query could not be run.</returns>
        private DataSet Query(string sql, SqlParameter[] param, string where)
        {
            try
            {
                return DB.ExecuteDataset(sql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_190 " + where + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>The single bind every product-scoped statement carries.</summary>
        private SqlParameter[] ProductParam(int M_Product_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_Product_ID", M_Product_ID) };
        }

        /// <summary>
        /// The application's business date for this session, falling back to the
        /// server date. Price-list versions are judged against this rather than
        /// against the database server's clock.
        /// </summary>
        private DateTime GetBusinessDate(Ctx ctx)
        {
            try
            {
                // The session's login date, held in context as milliseconds — the
                // same route the platform's own processes take to it.
                DateTime? contextDate =
                    CommonFunctions.CovertMilliToDate(ctx.GetContextAsTime("#Date"));
                if (contextDate.HasValue && contextDate.Value != DateTime.MinValue)
                    return contextDate.Value;
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_190 GetBusinessDate: " + ex.Message);
            }
            return DateTime.Now;
        }

        /// <summary>Resolves an AD_Table_ID by table name (0 when not found).</summary>
        private int GetTableId(string tableName)
        {
            try
            {
                string sql = @"SELECT t.AD_Table_ID FROM AD_Table t
                               WHERE UPPER(t.TableName)=UPPER(@TableName)
                                 AND t.IsActive='Y'";
                return Util.GetValueOfInt(DB.ExecuteScalar(sql,
                    new SqlParameter[] { new SqlParameter("@TableName", tableName) }, null));
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_190 GetTableId(" + tableName + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Returns the first of the candidate columns that exists on the table, or
        /// an empty string when the table has none of them.
        /// </summary>
        private string FindDisplayColumn(string tableName, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i])) return candidates[i];
            }
            return "";
        }

        /// <summary>
        /// True when the table exists in the AD_Table dictionary. A lookup problem
        /// degrades to "absent", which simply hides the optional section that
        /// depends on it.
        /// </summary>
        private bool TableExists(string tableName)
        {
            try
            {
                string sql = @"SELECT COUNT(1) FROM AD_Table t
                               WHERE UPPER(t.TableName)=UPPER(@TableName)";
                return Util.GetValueOfInt(DB.ExecuteScalar(sql,
                    new SqlParameter[] { new SqlParameter("@TableName", tableName) }, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_190 TableExists(" + tableName + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// True when the column exists on the table, using the AD_Column
        /// dictionary. A lookup problem degrades to "absent" so it can only ever
        /// remove an optional expression, never break a query.
        /// </summary>
        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                string sql = @"SELECT COUNT(1) FROM AD_Column c
                               INNER JOIN AD_Table t ON (t.AD_Table_ID=c.AD_Table_ID)
                               WHERE UPPER(c.ColumnName)=UPPER(@ColumnName)
                                 AND UPPER(t.TableName)=UPPER(@TableName)";
                // Two binds, each occurring once, in the order they appear.
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@TableName", tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_190 ColumnExists(" + tableName + "." + columnName + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// A decimal that keeps the difference between "not configured" and
        /// "zero" — a quality minimum of 0 is a real specification, a missing one
        /// is not.
        /// </summary>
        private static decimal? NullableDecimal(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Util.GetValueOfDecimal(value);
        }

        /// <summary>
        /// Renders a list of ids as a comma-separated SQL list. Only ever called
        /// with integers this model read out of the database, so no user input
        /// reaches the statement.
        /// </summary>
        private static string JoinIds(List<int> ids)
        {
            string[] parts = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) parts[i] = ids[i].ToString();
            return string.Join(",", parts);
        }

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        /// <summary>The client's primary accounting schema.</summary>
        private class AcctSchemaInfo
        {
            public int    C_AcctSchema_ID { get; set; }
            public string CostingMethod   { get; set; }
            public string CurrencyISO     { get; set; }
            public string CurSymbol       { get; set; }
            public int    StdPrecision    { get; set; }
        }

        /// <summary>One resolved account combination.</summary>
        private class ValidCombinationInfo
        {
            public string Combination { get; set; }
            public string Description { get; set; }
        }

        public class ProductSummaryData
        {
            public int       M_Product_ID          { get; set; }
            public string    Name                  { get; set; }
            public string    Code                  { get; set; }   // M_Product.Value
            public string    SKU                   { get; set; }
            public string    Barcode               { get; set; }   // M_Product.UPC
            public string    ProductType           { get; set; }   // I | S | R | E
            public string    StatusCode            { get; set; }   // ACTIVE | INACTIVE | DISCONTINUED
            public bool      IsActiveRecord        { get; set; }
            public bool      IsDiscontinued        { get; set; }
            public DateTime? DiscontinuedFrom      { get; set; }
            public bool      IsBOM                 { get; set; }
            public bool      IsVerified            { get; set; }
            public int       M_Product_Category_ID { get; set; }
            public string    CategoryName          { get; set; }
            public int       M_AttributeSet_ID     { get; set; }
            public int       C_UOM_ID              { get; set; }
            public string    BaseUomName           { get; set; }
            public int       UomPrecision          { get; set; }
            public int       AD_Image_ID           { get; set; }
            public string    ImageUrl              { get; set; }
            public int       VA010_QualityPlan_ID  { get; set; }
        }

        /// <summary>One attribute-set control or instance attribute.</summary>
        public class AttributeRowData
        {
            public string AttributeSetName { get; set; }
            public string Name             { get; set; }   // control key or attribute name
            public string Kind             { get; set; }   // control | instance
            public string ChipKey          { get; set; }   // ON | MANDATORY | OPTIONAL
            public string ValueType        { get; set; }
            public int    ValueCount       { get; set; }
            public int    GuaranteeDays    { get; set; }
        }

        public class TaxData
        {
            public string TaxCategoryName { get; set; }
            public string HsnSacCode      { get; set; }
        }

        public class StockSummaryData
        {
            public decimal OnHandQty          { get; set; }
            public decimal ReservedQty        { get; set; }
            public decimal OnOrderQty         { get; set; }
            public decimal AvailableToPromise { get; set; }
            public int     ReservedOrderCount { get; set; }
            public int     WarehouseCount     { get; set; }
            public int     LocatorCount       { get; set; }
        }

        public class StockRowData
        {
            public int      M_Warehouse_ID              { get; set; }
            public string   WarehouseName               { get; set; }
            public int      M_Locator_ID                { get; set; }
            public string   LocatorName                 { get; set; }
            public int      M_AttributeSetInstance_ID   { get; set; }
            public string   Attributes                  { get; set; }
            public decimal  QtyOnHand                   { get; set; }
        }

        public class UomConversionData
        {
            public int     C_UOM_To_ID       { get; set; }
            public string  UomName           { get; set; }
            public int     UomPrecision      { get; set; }
            public string  BaseUomName       { get; set; }
            public decimal DivideRate        { get; set; }
            public decimal MultiplyRate      { get; set; }
            /// <summary>How many BASE units one of this unit is worth.</summary>
            public decimal RateToBase        { get; set; }
            public bool    IsProductSpecific { get; set; }
        }

        public class PricingRowData
        {
            public int       M_PriceList_ID { get; set; }
            public string    PriceListName  { get; set; }
            public string    VersionName    { get; set; }
            public DateTime? ValidFrom      { get; set; }
            public string    ISO_Code       { get; set; }
            public string    CurSymbol      { get; set; }
            public int       CurPrecision   { get; set; }
            public decimal   PriceList      { get; set; }
            public decimal   PriceStd       { get; set; }
            public decimal   PriceLimit     { get; set; }
        }

        public class BomRowData
        {
            public string  Kind            { get; set; }   // own | usedin
            public int     M_BOM_ID        { get; set; }
            public string  Name            { get; set; }
            public string  Description     { get; set; }
            public int     ComponentCount  { get; set; }   // own
            public int     ParentProductId { get; set; }   // usedin
            public decimal QtyPerParent    { get; set; }   // usedin
            public bool    IsVerified      { get; set; }
        }

        public class QualityParamRowData
        {
            public string   PlanName             { get; set; }
            public string   PlanDescription      { get; set; }
            public int      LineNo               { get; set; }
            public string   ParameterName        { get; set; }
            public string   ParameterDescription { get; set; }
            public decimal? MinValue             { get; set; }
            public decimal? MaxValue             { get; set; }
            public string   Observation          { get; set; }
            public string   ParameterType        { get; set; }
            public decimal? Weightage            { get; set; }
            public string   ListValue            { get; set; }
            public string   AssignedDescription  { get; set; }
        }

        public class SupplierRowData
        {
            public int       C_BPartner_ID        { get; set; }
            public string    VendorName           { get; set; }
            public bool      IsCurrentVendor      { get; set; }
            public string    VendorProductNo      { get; set; }
            public int       DeliveryTimePromised { get; set; }
            public decimal?  PriceLastPO          { get; set; }
            public DateTime? PriceLastPODate      { get; set; }
            public string    ISO_Code             { get; set; }
            public string    CurSymbol            { get; set; }
            public int       CurPrecision         { get; set; }
        }

        public class OrderRowData
        {
            public int       C_Order_ID   { get; set; }
            public string    DocumentNo   { get; set; }
            public string    DocStatus    { get; set; }   // raw code; the panel labels it
            public DateTime? DateOrdered  { get; set; }
            public string    BPartnerName { get; set; }
            public decimal   Qty          { get; set; }
            public decimal   LineNetAmt   { get; set; }
            public string    UomName      { get; set; }
            public string    CurSymbol    { get; set; }
            public string    ISO_Code     { get; set; }
            public int       CurPrecision { get; set; }
            public bool      IsSOTrx      { get; set; }
        }

        public class TransactionRowData
        {
            public int       M_Transaction_ID { get; set; }
            public string    MovementType     { get; set; }
            public DateTime? MovementDate     { get; set; }
            public decimal   MovementQty      { get; set; }
            public string    DocumentNo       { get; set; }
            /// <summary>The source document's own C_DocType name, shown before the number.</summary>
            public string    DocTypeName      { get; set; }
            /// <summary>Table + id the row navigates to: M_InOut, M_Inventory or M_Movement.</summary>
            public string    DocTableName     { get; set; }
            public int       DocRecordId      { get; set; }
            /// <summary>Null where the movement genuinely has no price.</summary>
            public decimal?  UnitPrice        { get; set; }
            public string    CurSymbol        { get; set; }
            public string    ISO_Code         { get; set; }
            public int       CurPrecision     { get; set; }
        }

        public class AccountRowData
        {
            public string AccountRole    { get; set; }   // the M_Product_Acct column name
            public string Combination    { get; set; }
            public string Description    { get; set; }
            /// <summary>True when the product had no override and the category answered.</summary>
            public bool   IsFromCategory { get; set; }
        }

        public class AccountingData
        {
            public string                CostingMethod { get; set; }
            public string                CurrencyISO   { get; set; }
            public string                CurSymbol     { get; set; }
            public List<AccountRowData>  Rows          { get; set; }
        }

        /// <summary>
        /// One timeline event. <see cref="Type"/> drives the chip and icon the
        /// client renders: fieldupdate | workflow | mail | note | task |
        /// appointment.
        /// </summary>
        public class ActivityData
        {
            public int       Id          { get; set; }
            public string    Type        { get; set; }
            public string    Title       { get; set; }
            public string    Actor       { get; set; }
            public DateTime? EventDate   { get; set; }

            // Field change
            public string    OldValue    { get; set; }
            public string    NewValue    { get; set; }

            // Workflow. StateCode is the stored list value; StateName is its
            // dictionary label in the reader's language — the panel shows the
            // name and only falls back to the code if the list is unresolved.
            public string    StateCode   { get; set; }
            public string    StateName   { get; set; }

            // Mail (revealed inline on click)
            public string    Body        { get; set; }
            public string    MailTo      { get; set; }
            public string    MailFrom    { get; set; }
            public string    MailCc      { get; set; }
            public string    MailBcc     { get; set; }
            public bool      IsSent      { get; set; }

            // Task / appointment
            public string    Location    { get; set; }
            public bool      IsClosed    { get; set; }
            public bool      IsCancelled { get; set; }
            public DateTime? StartDate   { get; set; }
            public DateTime? EndDate     { get; set; }
        }

        /// <summary>
        /// The whole payload. A section that does not apply to the product's type,
        /// or that has nothing in it, comes back null / empty and the panel omits
        /// it entirely — there is no "no data" row anywhere except Activity, which
        /// reports "0 events".
        /// </summary>
        public class ProductOverviewData
        {
            public ProductSummaryData          Product        { get; set; }
            public List<AttributeRowData>      Attributes     { get; set; }
            public TaxData                     Tax            { get; set; }
            public StockSummaryData            StockSummary   { get; set; }
            public List<StockRowData>          StockDetails   { get; set; }
            public List<UomConversionData>     UomConversions { get; set; }
            public List<PricingRowData>        Pricing        { get; set; }
            public List<BomRowData>            Manufacturing  { get; set; }
            public List<QualityParamRowData>   Quality        { get; set; }
            public List<SupplierRowData>       Suppliers      { get; set; }
            public List<OrderRowData>          SalesOrders    { get; set; }
            public List<OrderRowData>          PurchaseOrders { get; set; }
            public List<TransactionRowData>    Transactions   { get; set; }
            public AccountingData              Accounting     { get; set; }
            public List<ActivityData>          Activity       { get; set; }
        }
    }
}
