/// <summary>
/// Module Name : VASLogic
/// Purpose     : Product Overview right tab panel data (read side). Returns a
///               read-only contextual summary of the selected M_Product:
///               identity + lifecycle hero, attribute-set controls, tax
///               classification, stock position and stock by locator, UOM
///               conversions, price lists, BOMs (own + where-used), the
///               configured quality parameters and the latest quality check,
///               vendors, the latest sales / purchase orders, the physical
///               movements, the effective posting accounts and a unified
///               activity timeline.
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
///   VAI163   2026-08-18  - A UOM conversion is flagged as the product's DEFAULT
///                          purchase, sales or consumable unit from the columns
///                          the tenant actually keeps them in
///                          (M_Product.VAS_PurchaseUOM_ID, VAS_SalesUOM_ID,
///                          VAS_ConsumableUOM_ID). They were read as
///                          C_UOM_Purchase_ID / C_UOM_Sales_ID, names no schema
///                          here has, so the dictionary check degraded them to
///                          NULL and no conversion was ever flagged.
///                        - A price row carries the UNIT and the ATTRIBUTE SET
///                          INSTANCE it is for (M_ProductPrice.C_UOM_ID,
///                          M_AttributeSetInstance_ID). A price list holds one
///                          price per unit and per attribute set, and reading
///                          neither meant those rows reached the panel as the
///                          same list repeated with different figures.
///                        - Accounting reports the product's OWN accounts only,
///                          under the first of the client's accounting schemas
///                          that carries a row for it. It read four columns under
///                          the primary schema and fell back to the product
///                          CATEGORY's accounts, which showed a product with an
///                          empty accounting tab its category's numbers as if
///                          they were its own, and left accounts that WERE set
///                          unreported — both the four it never asked about and
///                          any kept under a second schema.
///                        - ColumnExists answers from a per-request cache: the
///                          accounting section asks about a dozen columns, once
///                          per schema it tries, and the dictionary cannot change
///                          while one panel is being built.
///   VAI163   2026-08-18  - Attributes reports what each control imposes rather
///                          than that it is switched on: IsLotMandatory and its
///                          two siblings, the M_LotCtl / M_SerNoCtl record the
///                          set names, the set's MandatoryType, and the shelf
///                          life from M_Product.GuaranteeDays with the set's as
///                          the fallback. IsInstanceAttribute is read PER
///                          ATTRIBUTE — the set's own flag only says that some
///                          attribute on it is an instance attribute, so it
///                          could not answer for any single one.
///                        - The purchase side lists PURCHASE ORDERS only
///                          (C_DocType.DocBaseType = 'POO', not a return type),
///                          so blanket purchase orders and vendor RMAs no longer
///                          appear as purchase orders — in the section or in the
///                          on-order figure. The sales side is untouched.
///                        - The reserved / on-order readers return how many OPEN
///                          orders each figure came from and, where that is one,
///                          that order's DateOrdered and DatePromised.
///   VAI163   2026-08-18  - An order row carries the ENTERED quantity (the one
///                          stated in the line's own unit) alongside the base
///                          ordered / delivered figures, and the fulfilment those
///                          two make: DUE, SHORT or FULL.
///                        - A transaction row carries where the movement happened
///                          (warehouse, locator, attribute set instance), the
///                          COST it was booked at — M_CostDetail against the very
///                          line it came from, cost element 0, primary schema,
///                          read as a correlated SUM so a line with several cost
///                          rows cannot repeat the transaction — and the name of
///                          the window its document lives on, worked out from the
///                          document's own flags. The section also reports the
///                          product's TOTAL transaction count, of which it
///                          carries the latest few.
///                        - Supplier rows are stamped from the purchase history:
///                          the latest purchase order per vendor (date, document,
///                          price), the single most recent of them flagged as the
///                          last used vendor and moved to the front. The stored
///                          PriceLastPO / PriceLastPODate are a fallback now —
///                          they are only written by the processes that maintain
///                          them, so on a tenant that does not, the last used
///                          vendor showed no purchase at all.
///   VAI163   2026-08-19  - Quality reports what was FOUND as well as what is
///                          configured: the latest CHECK on the product
///                          (VA010_ShipConfParameters on a receipt confirmation,
///                          VA010_MoveConfParameters on a transfer's), with the
///                          document it was raised on and every parameter read on
///                          it. It is not hung off the quality plan — a product
///                          whose plan has since been cleared has still been
///                          checked. The configured parameters no longer carry
///                          MinValue / MaxValue: those are a numeric tolerance
///                          nearly every parameter leaves unset, so the section
///                          printed "Min 0 · Max 0" against tests that have no
///                          numeric range at all.
///                        - Pricing reads EVERY version of every price list the
///                          product is priced on, not the one in force per list.
///                          A product priced on several versions of one list
///                          showed exactly one of them with nothing saying the
///                          rest existed. Which version is in force is still
///                          answered, per list, by IsCurrentVersion.
///                        - A where-used BOM row is the DETAIL LINE, and reports
///                          what that line says: the attribute set instance it is
///                          specified for and when it was created. Both BOM
///                          sources are ordered together, newest first, since the
///                          section pages.
///                        - Recent transactions carry the product's WHOLE
///                          movement history — the same set its Transaction tab
///                          lists — rather than the latest twenty, which left the
///                          rest unreachable from the panel.
///                        - The activity trail is de-duplicated across its
///                          sources and its sort has a tie-break. List.Sort is
///                          unstable, so entries sharing a second could swap
///                          places between reads, which on a paged feed reads as
///                          one record appearing twice.
///   VAI163   2026-08-19  - The Manufacturing section is no longer gated on
///                          M_Product.IsBOM. That flag says the product is
///                          ASSEMBLED FROM components and says nothing about
///                          whether it is CONSUMED BY anything — so a raw
///                          material or bought part, which is exactly the product
///                          somebody asks "what is this used in" about, showed no
///                          Manufacturing section at all and its where-used rows
///                          were never read.
///                        - The SALES side lists sales orders proper, the way the
///                          purchase side already listed purchase orders. It was
///                          every sales-direction document the product appeared
///                          on, so quotations, proposals, blanket sales orders
///                          and customer RMAs all sat in a section labelled
///                          "Sales orders". A customer RMA cannot be told apart
///                          by sub-type — its seeded document type carries the
///                          same DocSubTypeSO 'SO' a standard order does — so the
///                          RETURN flag is what excludes it. The same filter now
///                          feeds the RESERVED figure, which was counting offers
///                          and blanket orders as commitments against stock.
///                        - An order row carries the order's own DatePromised, so
///                          a row with something still to move can say when it is
///                          due.
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

        /// <summary>C_DocType.DocBaseType of a purchase order proper. A blanket
        /// purchase order carries 'BOO' and a requisition 'POR', so neither is
        /// one — see <see cref="OrderDocTypeJoin"/>.</summary>
        private const string DOCBASETYPE_PURCHASEORDER = "POO";

        /// <summary>C_DocType.DocBaseType of a sales order proper.</summary>
        private const string DOCBASETYPE_SALESORDER = "SOO";

        /// <summary>
        /// The C_DocType.DocSubTypeSO values that are NOT a sales order, though
        /// they are all filed under DocBaseType 'SOO':
        ///   BO - blanket / release order (commits a price, orders nothing)
        ///   OB - binding offer     (a quotation)
        ///   ON - non-binding offer (a proposal)
        /// Exactly the exclusion list the payment-schedule and receipt readers
        /// already use (PoReceiptTabPanelModel, ExpectedThisWeekController), so
        /// "a sales order" means the same thing on this panel as it does
        /// everywhere else in the solution. What survives it — SO, PR, WR, WP,
        /// WI — is every genuinely order-like sub-type.
        ///
        /// A CUSTOMER RMA is not in the list, and cannot be: its seeded document
        /// type carries DocSubTypeSO 'SO', the SAME value a standard order has
        /// (MSetup seeds it as SALESORDER + StandardOrder + IsReturnTrx). The
        /// RETURN FLAG is its only discriminator, which is why the join below
        /// tests that separately. 'RM' is declared in the model layer but no
        /// document type is ever created with it, so filtering on it would
        /// exclude nothing.
        /// </summary>
        private static readonly string[] DOCSUBTYPESO_NOT_A_SALESORDER =
            new string[] { "BO", "OB", "ON" };

        /// <summary>The four supported product types. Nothing else is queried.</summary>
        private const string TYPE_ITEM     = "I";
        private const string TYPE_SERVICE  = "S";
        private const string TYPE_RESOURCE = "R";
        private const string TYPE_EXPENSE  = "E";

        /// <summary>Latest-N caps. The panel is contextual: it shows recent
        /// history, not all of it. Transactions are the exception — the section
        /// carries the product's whole movement history, which is what its
        /// Transaction tab holds, and pages through it.</summary>
        private const int MAX_ORDERS       = 5;
        private const int MAX_ACTIVITY     = 60;

        /// <summary>M_Product's AD_Table_ID, resolved once per request and inlined
        /// into the activity queries as an integer literal. Inlining keeps each
        /// statement down to a single bind name, which positional binding needs.</summary>
        private int _productTableId;

        /// <summary>Dictionary answers already given this request, keyed
        /// TABLE.COLUMN. See <see cref="ColumnExists"/>.</summary>
        private readonly Dictionary<string, bool> _columnExists = new Dictionary<string, bool>();

        /// <summary>The client's accounting schemas, read once per request —
        /// see <see cref="LoadAcctSchemas"/>. Null until the first read.</summary>
        private List<AcctSchemaInfo> _acctSchemas;

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
            result.Attributes     = LoadAttributes(ctx, result.Product.M_AttributeSet_ID,
                                                   result.Product.GuaranteeDays);
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
                // Not gated on M_Product.IsBOM. That flag says the product is
                // ASSEMBLED FROM components; it says nothing about whether it is
                // CONSUMED BY anything — and the where-used rows live in this
                // same section. Gating on it meant a raw material or a bought
                // part, which is exactly the product a reader asks "what is this
                // used in" about, showed no Manufacturing section at all.
                // LoadBoms comes back empty when there is neither, and an empty
                // section is not drawn.
                result.Manufacturing = LoadBoms(ctx, M_Product_ID);
                if (result.Product.VA010_QualityPlan_ID > 0)
                {
                    result.Quality = LoadQualityParams(ctx, result.Product.VA010_QualityPlan_ID);
                }
                // What was actually FOUND, which is not read from the plan: a
                // product whose plan has since been cleared has still been
                // checked, so this does not hang off the plan id.
                result.QualityCheck = LoadLatestQualityCheck(ctx, M_Product_ID);
                result.Transactions     = LoadTransactions(ctx, M_Product_ID);
                result.TransactionTotal = CountTransactions(ctx, M_Product_ID);
            }

            // ----- Accounting: only what the product's own tab actually sets -----
            result.Accounting = LoadAccounting(ctx, M_Product_ID, type);

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
            // The shelf life kept on the product's own stock tab. It overrides
            // the attribute set's guarantee days, which is the order the
            // platform's own attribute plumbing reads them in.
            bool hasGuaranteeDays  = ColumnExists("M_Product", "GuaranteeDays");

            string discontinuedExpr   = hasDiscontinued
                ? "COALESCE(p.Discontinued, 'N')" : "'N'";
            string discontinuedAtExpr = hasDiscontinuedBy
                ? "p.DiscontinuedBy" : "CAST(NULL AS DATE)";
            string qualityPlanExpr    = hasQualityPlan
                ? "COALESCE(p.VA010_QualityPlan_ID, 0)" : "0";
            string guaranteeDaysExpr  = hasGuaranteeDays
                ? "COALESCE(p.GuaranteeDays, 0)" : "0";
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
                                  " + guaranteeDaysExpr + @" AS GuaranteeDays,
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
            s.GuaranteeDays         = Util.GetValueOfInt(r["GuaranteeDays"]);

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
        ///
        /// Each control says whether it is MANDATORY or optional (IsLotMandatory
        /// and its two siblings) rather than merely that it is switched on, and
        /// the lot / serial controls name the control record the set points at
        /// (M_LotCtl, M_SerNoCtl) where it names one. The expiry control carries
        /// the shelf life: the PRODUCT's own GuaranteeDays first — that is the
        /// figure kept on its stock tab — and the set's only where the product
        /// leaves it unset, which is the order MAttributeSet's own callers read
        /// them in.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="M_AttributeSet_ID">The product's attribute set; 0 when none.</param>
        /// <param name="productGuaranteeDays">M_Product.GuaranteeDays; 0 when unset.</param>
        /// <returns>Attribute rows, or an empty list when the set has no controls.</returns>
        private List<AttributeRowData> LoadAttributes(Ctx ctx, int M_AttributeSet_ID,
                                                      int productGuaranteeDays)
        {
            List<AttributeRowData> rows = new List<AttributeRowData>();
            if (M_AttributeSet_ID <= 0) return rows;

            // The named lot / serial control records are optional on the set, so
            // both the column and the table they point at are resolved through the
            // dictionary before they are joined.
            bool hasLotCtl   = ColumnExists("M_AttributeSet", "M_LotCtl_ID")
                               && TableExists("M_LotCtl");
            bool hasSerNoCtl = ColumnExists("M_AttributeSet", "M_SerNoCtl_ID")
                               && TableExists("M_SerNoCtl");
            string lotCtlExpr   = hasLotCtl   ? "lotc.Name"  : "CAST(NULL AS VARCHAR(60))";
            string serNoCtlExpr = hasSerNoCtl ? "serc.Name"  : "CAST(NULL AS VARCHAR(60))";
            string lotCtlJoin   = hasLotCtl
                ? " LEFT OUTER JOIN M_LotCtl lotc ON (lotc.M_LotCtl_ID=aset.M_LotCtl_ID)" : "";
            string serNoCtlJoin = hasSerNoCtl
                ? " LEFT OUTER JOIN M_SerNoCtl serc ON (serc.M_SerNoCtl_ID=aset.M_SerNoCtl_ID)" : "";

            // --- The set's own control flags ---
            string setSql = @"SELECT aset.M_AttributeSet_ID,
                                     aset.Name AS AttributeSetName,
                                     aset.MandatoryType,
                                     COALESCE(aset.IsLot, 'N') AS IsLot,
                                     COALESCE(aset.IsLotMandatory, 'N') AS IsLotMandatory,
                                     COALESCE(aset.IsSerNo, 'N') AS IsSerNo,
                                     COALESCE(aset.IsSerNoMandatory, 'N') AS IsSerNoMandatory,
                                     COALESCE(aset.IsGuaranteeDate, 'N') AS IsGuaranteeDate,
                                     COALESCE(aset.IsGuaranteeDateMandatory, 'N') AS IsGuaranteeDateMandatory,
                                     COALESCE(aset.GuaranteeDays, 0) AS GuaranteeDays,
                                     COALESCE(aset.IsInstanceAttribute, 'N') AS IsInstanceAttribute,
                                     " + lotCtlExpr + @" AS LotCtlName,
                                     " + serNoCtlExpr + @" AS SerNoCtlName
                              FROM M_AttributeSet aset" + lotCtlJoin + serNoCtlJoin + @"
                              WHERE aset.M_AttributeSet_ID=@M_AttributeSet_ID
                                AND aset.IsActive='Y'";
            setSql = MRole.GetDefault(ctx).AddAccessSQL(
                setSql, "aset", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(setSql,
                new SqlParameter[] { new SqlParameter("@M_AttributeSet_ID", M_AttributeSet_ID) },
                "LoadAttributes(set)");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return rows;

            DataRow r = ds.Tables[0].Rows[0];
            string setName          = Util.GetValueOfString(r["AttributeSetName"]);
            string setMandatoryType = Util.GetValueOfString(r["MandatoryType"]);

            if (Util.GetValueOfString(r["IsLot"]) == "Y")
            {
                rows.Add(new AttributeRowData
                {
                    Name        = "LOT",
                    Kind        = "control",
                    ChipKey     = Util.GetValueOfString(r["IsLotMandatory"]) == "Y"
                                    ? "MANDATORY" : "OPTIONAL",
                    ControlName = Util.GetValueOfString(r["LotCtlName"])
                });
            }
            if (Util.GetValueOfString(r["IsSerNo"]) == "Y")
            {
                rows.Add(new AttributeRowData
                {
                    Name        = "SERNO",
                    Kind        = "control",
                    ChipKey     = Util.GetValueOfString(r["IsSerNoMandatory"]) == "Y"
                                    ? "MANDATORY" : "OPTIONAL",
                    ControlName = Util.GetValueOfString(r["SerNoCtlName"])
                });
            }
            if (Util.GetValueOfString(r["IsGuaranteeDate"]) == "Y")
            {
                // The product's shelf life wins; the set's stands in for it.
                int guaranteeDays = productGuaranteeDays > 0
                    ? productGuaranteeDays : Util.GetValueOfInt(r["GuaranteeDays"]);
                rows.Add(new AttributeRowData
                {
                    Name          = "GUARANTEEDATE",
                    Kind          = "control",
                    ChipKey       = Util.GetValueOfString(r["IsGuaranteeDateMandatory"]) == "Y"
                                      ? "MANDATORY" : "OPTIONAL",
                    GuaranteeDays = guaranteeDays
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
                                      COALESCE(a.IsInstanceAttribute, 'N') AS IsInstanceAttribute,
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
                                        ? "MANDATORY" : "OPTIONAL",
                        // Read PER ATTRIBUTE, not from the set: the set's own flag
                        // only says that at least one of its attributes is an
                        // instance attribute, so calling every row one was wrong
                        // for the rest of them.
                        IsInstanceAttribute =
                            Util.GetValueOfString(ar["IsInstanceAttribute"]) == "Y"
                    });
                }
            }

            // The set name and its mandatory type ride on every row's owner
            // fields so the panel can put them in the section summary without a
            // second payload member.
            foreach (AttributeRowData row in rows)
            {
                row.AttributeSetName = setName;
                row.SetMandatoryType = setMandatoryType;
            }
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

            // --- Reserved: still-open quantity on completed SALES orders ---
            OutstandingOrderInfo reserved = LoadOutstandingOrderQty(ctx, M_Product_ID, true);
            s.ReservedQty           = reserved.Qty;
            s.ReservedOrderCount    = reserved.OrderCount;
            s.ReservedDateOrdered   = reserved.DateOrdered;
            s.ReservedDatePromised  = reserved.DatePromised;

            // --- On order: still-open quantity on completed PURCHASE orders ---
            OutstandingOrderInfo onOrder = LoadOutstandingOrderQty(ctx, M_Product_ID, false);
            s.OnOrderQty            = onOrder.Qty;
            s.OnOrderCount          = onOrder.OrderCount;
            s.OnOrderDateOrdered    = onOrder.DateOrdered;
            s.OnOrderDatePromised   = onOrder.DatePromised;

            // The one availability formula this panel uses.
            s.AvailableToPromise = s.OnHandQty - s.ReservedQty;
            return s;
        }

        /// <summary>
        /// Still-OPEN (ordered less delivered) quantity for the product across
        /// completed orders of one direction, with the number of such orders and,
        /// where there is exactly one, its ordered and promised dates.
        ///
        /// Only DocStatus 'CO' contributes — drafted, in-progress, closed, voided
        /// and reversed documents reserve nothing and order nothing — and only
        /// lines with more ordered than delivered, which is what makes an order
        /// OPEN. Return transactions are excluded, and on the purchase side so
        /// are blanket orders and anything that is not a purchase order proper:
        /// a blanket order commits a price, it does not put stock on order.
        ///
        /// The two dates are aggregates over the whole set, so they only describe
        /// the figure when the set holds ONE order; the caller is what enforces
        /// that, by showing them only when the count is one.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="M_Product_ID">Selected product id.</param>
        /// <param name="isSalesOrder">True for reserved (SO), false for on order (PO).</param>
        /// <returns>Quantity, order count and the single order's dates.</returns>
        private OutstandingOrderInfo LoadOutstandingOrderQty(Ctx ctx, int M_Product_ID,
                                                             bool isSalesOrder)
        {
            OutstandingOrderInfo info = new OutstandingOrderInfo();
            // The flag is a stored column value, so it is compared as a plain
            // literal — never with the national-character prefix.
            string soTrx = isSalesOrder ? "Y" : "N";

            string sql = @"SELECT COALESCE(SUM(COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)), 0) AS OutstandingQty,
                                  COUNT(DISTINCT o.C_Order_ID) AS OrderCount,
                                  MIN(o.DateOrdered) AS DateOrdered,
                                  MIN(o.DatePromised) AS DatePromised
                           FROM C_Order o
                           INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID
                                                         AND ol.IsActive='Y')"
                           // Orders proper on BOTH sides now. The sales figure
                           // counted quotations, proposals and blanket sales
                           // orders as reservations — none of those commits stock
                           // — so Reserved could exceed everything actually sold.
                           + OrderDocTypeJoin(isSalesOrder) + @"
                           WHERE ol.M_Product_ID=@M_Product_ID
                             AND o.IsActive='Y'
                             AND o.IsSOTrx='" + soTrx + "'"
                           + OrderRowPredicates(isSalesOrder) + @"
                             AND o.DocStatus='CO'
                             AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(sql, ProductParam(M_Product_ID),
                               "LoadOutstandingOrderQty(" + soTrx + ")");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return info;

            DataRow r = ds.Tables[0].Rows[0];
            info.Qty        = Util.GetValueOfDecimal(r["OutstandingQty"]);
            info.OrderCount = Util.GetValueOfInt(r["OrderCount"]);
            if (info.OrderCount == 1)
            {
                info.DateOrdered  = Util.GetValueOfDateTime(r["DateOrdered"]);
                info.DatePromised = Util.GetValueOfDateTime(r["DatePromised"]);
            }
            return info;
        }

        /// <summary>
        /// The join that keeps an order statement to ORDERS PROPER of one
        /// direction. The document type behind the order has to be one, which is
        /// the only thing that can tell them apart: every one of these documents
        /// is a C_Order row with the same IsSOTrx.
        ///
        /// On the purchase side it leaves out blanket purchase orders (base type
        /// BOO), requisitions and vendor returns. On the sales side it leaves out
        /// blanket sales orders, quotations and proposals — all three are filed
        /// under base type SOO and separated only by DocSubTypeSO — and customer
        /// RMAs, which are a standard order carrying a return document type.
        ///
        /// The type is taken from the order's own C_DocType_ID, falling back to
        /// the target type a document that has not been completed yet carries.
        /// An empty string on a schema without the columns, so the statement
        /// simply keeps its previous reach.
        /// </summary>
        /// <param name="isSalesOrder">True for the sales side, false for purchase.</param>
        private string OrderDocTypeJoin(bool isSalesOrder)
        {
            if (!ColumnExists("C_DocType", "DocBaseType")) return "";

            bool hasTarget     = ColumnExists("C_Order", "C_DocTypeTarget_ID");
            bool hasTypeReturn = ColumnExists("C_DocType", "IsReturnTrx");
            bool hasSubType    = ColumnExists("C_DocType", "DocSubTypeSO");

            string typeExpr = hasTarget
                ? "COALESCE(NULLIF(o.C_DocType_ID, 0), o.C_DocTypeTarget_ID)"
                : "o.C_DocType_ID";
            string returnPredicate = hasTypeReturn
                ? " AND COALESCE(odt.IsReturnTrx, 'N')='N'" : "";

            // The sub-type only classifies sales documents, so it is only asked
            // about on that side. A stored code is compared as a plain literal —
            // never with the national-character prefix.
            string subTypePredicate = "";
            if (isSalesOrder && hasSubType)
            {
                subTypePredicate = " AND COALESCE(odt.DocSubTypeSO, ' ') NOT IN ('"
                                 + string.Join("','", DOCSUBTYPESO_NOT_A_SALESORDER) + "')";
            }

            string baseType = isSalesOrder
                ? DOCBASETYPE_SALESORDER : DOCBASETYPE_PURCHASEORDER;

            return @" INNER JOIN C_DocType odt ON (odt.C_DocType_ID=" + typeExpr
                   + " AND odt.DocBaseType='" + baseType + "'"
                   + returnPredicate + subTypePredicate + ")";
        }

        /// <summary>
        /// The predicates the ORDER ROW itself has to satisfy to be an order
        /// proper, on top of what its document type says.
        ///
        /// C_Order denormalises the three exclusions onto the row —
        /// IsSalesQuotation, IsBlanketTrx, IsReturnTrx — and that is what the
        /// account and quotation panels (VAS_105, VAS_123) filter on. They are
        /// applied here as well as the document-type join, not instead of it: the
        /// flags catch a row whose document type a tenant has left unclassified,
        /// and the join catches a row whose flags were never written. Each is
        /// resolved through the dictionary, so a schema without one simply keeps
        /// the other.
        ///
        /// The return flag applies to both directions — a vendor RMA is no more a
        /// purchase order than a customer RMA is a sales order.
        /// </summary>
        private string OrderRowPredicates(bool isSalesOrder)
        {
            StringBuilder predicates = new StringBuilder();
            if (ColumnExists("C_Order", "IsReturnTrx"))
            {
                predicates.Append(" AND COALESCE(o.IsReturnTrx, 'N')='N'");
            }
            if (isSalesOrder)
            {
                if (ColumnExists("C_Order", "IsSalesQuotation"))
                {
                    predicates.Append(" AND COALESCE(o.IsSalesQuotation, 'N')='N'");
                }
                if (ColumnExists("C_Order", "IsBlanketTrx"))
                {
                    predicates.Append(" AND COALESCE(o.IsBlanketTrx, 'N')='N'");
                }
            }
            return predicates.ToString();
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
            // Which unit the product is BOUGHT, SOLD and CONSUMED in. All three
            // are optional columns on M_Product — a schema without one simply
            // flags no row for it — and they are what tells the reader which of a
            // product's conversions its documents will actually be keyed in.
            // Without them every row read only "product-specific" or "generic",
            // which says how the conversion is DEFINED, never what it is FOR.
            //
            // The names are the tenant's own (VAS_PurchaseUOM_ID and friends,
            // written by the product UOM setup screen). They were read as
            // C_UOM_Purchase_ID / C_UOM_Sales_ID, which exist in no schema here,
            // so the lookup silently degraded and no row was ever flagged.
            bool hasPurchaseUom   = ColumnExists("M_Product", "VAS_PurchaseUOM_ID");
            bool hasSalesUom      = ColumnExists("M_Product", "VAS_SalesUOM_ID");
            bool hasConsumableUom = ColumnExists("M_Product", "VAS_ConsumableUOM_ID");
            string purchaseUomExpr   = hasPurchaseUom   ? "p.VAS_PurchaseUOM_ID"   : "NULL";
            string salesUomExpr      = hasSalesUom      ? "p.VAS_SalesUOM_ID"      : "NULL";
            string consumableUomExpr = hasConsumableUom ? "p.VAS_ConsumableUOM_ID" : "NULL";

            string sql = @"SELECT conv.C_UOM_Conversion_ID,
                                  conv.M_Product_ID,
                                  conv.C_UOM_To_ID,
                                  uto.Name AS ConversionUomName,
                                  COALESCE(uto.StdPrecision, 0) AS UomPrecision,
                                  COALESCE(conv.DivideRate, 0) AS DivideRate,
                                  COALESCE(conv.MultiplyRate, 0) AS MultiplyRate,
                                  " + purchaseUomExpr + @"   AS PurchaseUomId,
                                  " + salesUomExpr + @"      AS SalesUomId,
                                  " + consumableUomExpr + @" AS ConsumableUomId
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
                c.IsPurchaseUom   = toUom > 0 && Util.GetValueOfInt(r["PurchaseUomId"])   == toUom;
                c.IsSalesUom      = toUom > 0 && Util.GetValueOfInt(r["SalesUomId"])      == toUom;
                c.IsConsumableUom = toUom > 0 && Util.GetValueOfInt(r["ConsumableUomId"]) == toUom;

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
        /// One row per PRICE the product has: every version of every price list
        /// that carries one, and within a version every unit and attribute set it
        /// is priced for. Cost is deliberately not read — this section shows
        /// selling / buying prices, and M_Cost is never queried.
        ///
        /// It used to report the currently applicable version of each list only,
        /// ranked by ROW_NUMBER, which meant a product priced on several versions
        /// of ONE list showed exactly one of them and the rest simply were not on
        /// screen. Nothing said they had been dropped, so the section read as the
        /// product's complete pricing when it was a single row out of many.
        ///
        /// Which version is the one in force is still answered — the row carries
        /// <see cref="PricingRowData.IsCurrentVersion"/>, worked out per price
        /// list from the business date — but it no longer decides what is READ.
        /// </summary>
        private List<PricingRowData> LoadPricing(Ctx ctx, int M_Product_ID)
        {
            List<PricingRowData> rows = new List<PricingRowData>();

            // The business date a version is judged against comes from the
            // application context, never from the database server's clock. It is
            // applied in managed code, so the statement keeps its single bind
            // name, which positional binding requires.
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
                             WHERE pl.IsActive='Y'";
            inner = MRole.GetDefault(ctx).AddAccessSQL(
                inner, "pl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // A price row is keyed by UNIT and by ATTRIBUTE SET INSTANCE as well
            // as by product, so one list can carry several prices for this
            // product. Both are read and reported: without them those rows reach
            // the panel as the same price list repeated with different figures.
            // Optional columns, resolved through the dictionary.
            bool hasPriceUom = ColumnExists("M_ProductPrice", "C_UOM_ID");
            bool hasPriceAsi = ColumnExists("M_ProductPrice", "M_AttributeSetInstance_ID");

            string priceUomExpr  = hasPriceUom ? "COALESCE(ppu.Name, '')" : "''";
            string priceUomJoin  = hasPriceUom
                ? " LEFT OUTER JOIN C_UOM ppu ON (ppu.C_UOM_ID=pp.C_UOM_ID)" : "";
            string priceAsiExprs = hasPriceAsi
                ? @"asi.Description AS AsiDescription,
                    asi.Lot,
                    asi.SerNo,
                    asi.GuaranteeDate"
                : @"CAST(NULL AS VARCHAR(255)) AS AsiDescription,
                    CAST(NULL AS VARCHAR(255)) AS Lot,
                    CAST(NULL AS VARCHAR(255)) AS SerNo,
                    CAST(NULL AS DATE) AS GuaranteeDate";
            string priceAsiJoin  = hasPriceAsi
                ? @" LEFT OUTER JOIN M_AttributeSetInstance asi
                       ON (asi.M_AttributeSetInstance_ID=pp.M_AttributeSetInstance_ID
                           AND pp.M_AttributeSetInstance_ID > 0)"
                : "";

            // The versions, joined to the product's own price rows. One bind name,
            // occurring once, which is what positional binding needs.
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
                                  COALESCE(pp.PriceLimit, 0) AS PriceLimit,
                                  " + priceUomExpr + @" AS PriceUomName,
                                  " + priceAsiExprs + @"
                           FROM (" + inner + @") rv
                           INNER JOIN M_ProductPrice pp ON (pp.M_PriceList_Version_ID=rv.M_PriceList_Version_ID
                                                            AND pp.IsActive='Y')"
                           + priceUomJoin + priceAsiJoin + @"
                           WHERE pp.M_Product_ID=@M_Product_ID
                           ORDER BY rv.PriceListName, rv.ValidFrom DESC,
                                    rv.M_PriceList_Version_ID DESC, "
                           + (hasPriceUom ? "ppu.Name" : "rv.VersionName")
                           + (hasPriceAsi ? ", pp.M_AttributeSetInstance_ID" : "");

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadPricing");
            if (ds == null || ds.Tables.Count == 0) return rows;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new PricingRowData
                {
                    M_PriceList_ID = Util.GetValueOfInt(r["M_PriceList_ID"]),
                    M_PriceList_Version_ID = Util.GetValueOfInt(r["M_PriceList_Version_ID"]),
                    PriceListName  = Util.GetValueOfString(r["PriceListName"]),
                    VersionName    = Util.GetValueOfString(r["VersionName"]),
                    ValidFrom      = Util.GetValueOfDateTime(r["ValidFrom"]),
                    ISO_Code       = Util.GetValueOfString(r["ISO_Code"]),
                    CurSymbol      = Util.GetValueOfString(r["CurSymbol"]),
                    CurPrecision   = Util.GetValueOfInt(r["CurPrecision"]),
                    PriceList      = Util.GetValueOfDecimal(r["PriceList"]),
                    PriceStd       = Util.GetValueOfDecimal(r["PriceStd"]),
                    PriceLimit     = Util.GetValueOfDecimal(r["PriceLimit"]),
                    UomName        = Util.GetValueOfString(r["PriceUomName"]),
                    // The same reader the stock rows use: the stored description
                    // first, lot / serial / guarantee only when there is none.
                    Attributes     = BuildAsiText(
                        Util.GetValueOfString(r["AsiDescription"]),
                        Util.GetValueOfString(r["Lot"]),
                        Util.GetValueOfString(r["SerNo"]),
                        Util.GetValueOfDateTime(r["GuaranteeDate"]))
                });
            }

            FlagCurrentPriceListVersions(rows, businessDate);
            return rows;
        }

        /// <summary>
        /// Marks the version of each price list that is IN FORCE on the business
        /// date: the latest one whose ValidFrom has been reached. Every row of
        /// that version is marked, since a version prices a product per unit and
        /// per attribute set and all of those rows are equally in force.
        ///
        /// A list all of whose versions are still in the future has none in force
        /// and nothing is marked — no version is promoted to fill the gap.
        /// </summary>
        private static void FlagCurrentPriceListVersions(List<PricingRowData> rows, DateTime businessDate)
        {
            // The version in force per price list.
            Dictionary<int, int> currentVersion = new Dictionary<int, int>();
            Dictionary<int, DateTime> currentFrom = new Dictionary<int, DateTime>();

            foreach (PricingRowData row in rows)
            {
                if (!row.ValidFrom.HasValue || row.ValidFrom.Value > businessDate) continue;

                int listId = row.M_PriceList_ID;
                if (!currentVersion.ContainsKey(listId)
                    || row.ValidFrom.Value > currentFrom[listId]
                    || (row.ValidFrom.Value == currentFrom[listId]
                        && row.M_PriceList_Version_ID > currentVersion[listId]))
                {
                    currentVersion[listId] = row.M_PriceList_Version_ID;
                    currentFrom[listId]    = row.ValidFrom.Value;
                }
            }

            foreach (PricingRowData row in rows)
            {
                row.IsCurrentVersion = currentVersion.ContainsKey(row.M_PriceList_ID)
                    && currentVersion[row.M_PriceList_ID] == row.M_PriceList_Version_ID;
            }
        }

        // ----------------------------------------------------------------- //
        //  8. Manufacturing - BOMs (Item only)                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The product's own BOMs and the BOMs it is consumed in, merged and
        /// ordered NEWEST FIRST on when the record itself was created. No default
        /// flag, no version and no version date are read — those were removed
        /// from the specification and are not queried at all.
        ///
        /// The two sources are ordered together rather than one after the other:
        /// the section pages, and a list that put every own BOM before every
        /// where-used row buried the recent ones behind pages of old ones.
        /// </summary>
        private List<BomRowData> LoadBoms(Ctx ctx, int M_Product_ID)
        {
            List<BomRowData> rows = new List<BomRowData>();
            if (!TableExists("M_BOM")) return rows;

            LoadOwnBoms(ctx, M_Product_ID, rows);
            LoadWhereUsedBoms(ctx, M_Product_ID, rows);

            // Newest first, on the record's own creation stamp, with its id as
            // the tie-break so two rows created in the same instant keep a fixed
            // order between refreshes and cannot drift across page boundaries.
            rows.Sort(delegate (BomRowData a, BomRowData b)
            {
                int byDate = b.Created.GetValueOrDefault(DateTime.MinValue)
                              .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue));
                return byDate != 0 ? byDate : b.RecordId.CompareTo(a.RecordId);
            });
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
                                  b.Created,
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
            sql += " ORDER BY b.Created DESC, b.M_BOM_ID DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadOwnBoms");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new BomRowData
                {
                    Kind           = "own",
                    M_BOM_ID       = Util.GetValueOfInt(r["M_BOM_ID"]),
                    RecordId       = Util.GetValueOfInt(r["M_BOM_ID"]),
                    Name           = Util.GetValueOfString(r["BomName"]),
                    Description    = Util.GetValueOfString(r["BomDescription"]),
                    ComponentCount = Util.GetValueOfInt(r["ComponentCount"]),
                    Created        = Util.GetValueOfDateTime(r["Created"]),
                    IsVerified     = Util.GetValueOfString(r["IsVerified"]) == "Y"
                });
            }
        }

        /// <summary>
        /// The BOMs that consume this product as a component.
        ///
        /// The row is the BOM DETAIL line (M_BOMProduct), so it reports what that
        /// line itself says: the attribute set instance it is specified for — a
        /// parent can consume this product under one attribute set and not
        /// another, and without it two lines of the same parent were the same row
        /// printed twice — and when the line was created.
        /// </summary>
        private void LoadWhereUsedBoms(Ctx ctx, int M_Product_ID, List<BomRowData> rows)
        {
            // The attribute set instance is optional on the detail line, so both
            // the column and the join behind it are resolved through the
            // dictionary first.
            bool hasAsi = ColumnExists("M_BOMProduct", "M_AttributeSetInstance_ID");
            string asiExprs = hasAsi
                ? @"asi.Description AS AsiDescription,
                    asi.Lot,
                    asi.SerNo,
                    asi.GuaranteeDate"
                : @"CAST(NULL AS VARCHAR(255)) AS AsiDescription,
                    CAST(NULL AS VARCHAR(255)) AS Lot,
                    CAST(NULL AS VARCHAR(255)) AS SerNo,
                    CAST(NULL AS DATE) AS GuaranteeDate";
            string asiJoin = hasAsi
                ? @" LEFT OUTER JOIN M_AttributeSetInstance asi
                       ON (asi.M_AttributeSetInstance_ID=bp.M_AttributeSetInstance_ID
                           AND bp.M_AttributeSetInstance_ID > 0)"
                : "";

            string sql = @"SELECT b.M_BOM_ID,
                                  b.Name AS BomName,
                                  bp.M_BOMProduct_ID,
                                  bp.Created,
                                  parent.M_Product_ID AS ParentProductId,
                                  parent.Name AS ParentProductName,
                                  COALESCE(parent.IsVerified, 'N') AS IsVerified,
                                  COALESCE(bp.BOMQty, 0) AS BomQty,
                                  " + asiExprs + @"
                           FROM M_BOMProduct bp
                           INNER JOIN M_BOM b ON (b.M_BOM_ID=bp.M_BOM_ID
                                                  AND b.IsActive='Y')
                           INNER JOIN M_Product parent ON (parent.M_Product_ID=b.M_Product_ID)"
                           + asiJoin + @"
                           WHERE bp.M_ProductBOM_ID=@M_Product_ID
                             AND bp.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY bp.Created DESC, bp.M_BOMProduct_ID DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadWhereUsedBoms");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                rows.Add(new BomRowData
                {
                    Kind             = "usedin",
                    M_BOM_ID         = Util.GetValueOfInt(r["M_BOM_ID"]),
                    RecordId         = Util.GetValueOfInt(r["M_BOMProduct_ID"]),
                    Name             = Util.GetValueOfString(r["ParentProductName"]),
                    ParentProductId  = Util.GetValueOfInt(r["ParentProductId"]),
                    QtyPerParent     = Util.GetValueOfDecimal(r["BomQty"]),
                    Created          = Util.GetValueOfDateTime(r["Created"]),
                    // The same reader the stock and price rows use: the stored
                    // description first, lot / serial / guarantee only where the
                    // instance has none.
                    Attributes       = BuildAsiText(
                        Util.GetValueOfString(r["AsiDescription"]),
                        Util.GetValueOfString(r["Lot"]),
                        Util.GetValueOfString(r["SerNo"]),
                        Util.GetValueOfDateTime(r["GuaranteeDate"])),
                    IsVerified       = Util.GetValueOfString(r["IsVerified"]) == "Y"
                });
            }
        }

        // ----------------------------------------------------------------- //
        //  9. Quality parameters (Item only) - SPECIFICATION ONLY            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The quality parameters CONFIGURED against the product's quality plan,
        /// which answers "what will be checked". What was actually FOUND is a
        /// separate read — see <see cref="LoadLatestQualityCheck"/> — and the two
        /// are never mixed into one row.
        ///
        /// No range bound is read. MinValue / MaxValue are a numeric tolerance
        /// that only a numeric test parameter can carry, and on the plans here
        /// they are unset on nearly every parameter, so the section printed
        /// "Min 0.00 · Max 0.00" against tests that have no numeric range at all.
        /// The parameter's own DESCRIPTION carries what it is checked against,
        /// and both descriptions are reported — see the caller.
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

        /// <summary>
        /// The most recent quality CHECK carried out on the product, with every
        /// parameter read on it.
        ///
        /// A check is raised per CONFIRMATION LINE, one row per test parameter,
        /// on whichever of the two confirmation flows applies:
        ///   * VA010_ShipConfParameters — a receipt / shipment confirmation
        ///     (M_InOutLineConfirm), which is where an incoming goods check lives;
        ///   * VA010_MoveConfParameters — an internal transfer's confirmation
        ///     (M_MovementLineConfirm).
        /// Both are read where the schema has them and the LATEST of the two wins,
        /// so a tenant that only uses one is not asked to have the other.
        ///
        /// The rows of a check are grouped in managed code rather than by a second
        /// round trip: the reader takes the latest few rows per table and keeps
        /// those belonging to the newest confirmation line, which is one check.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="M_Product_ID">Selected product id.</param>
        /// <returns>The latest check, or null when the product has never been checked.</returns>
        private QualityCheckData LoadLatestQualityCheck(Ctx ctx, int M_Product_ID)
        {
            QualityCheckData receipt  = LoadLatestReceiptQualityCheck(ctx, M_Product_ID);
            QualityCheckData movement = LoadLatestMovementQualityCheck(ctx, M_Product_ID);

            if (receipt == null)  return movement;
            if (movement == null) return receipt;

            DateTime r = receipt.CheckDate.GetValueOrDefault(DateTime.MinValue);
            DateTime m = movement.CheckDate.GetValueOrDefault(DateTime.MinValue);
            return m > r ? movement : receipt;
        }

        /// <summary>The latest check raised on a receipt / shipment confirmation.</summary>
        private QualityCheckData LoadLatestReceiptQualityCheck(Ctx ctx, int M_Product_ID)
        {
            if (!TableExists("VA010_ShipConfParameters")) return null;

            string sql = @"SELECT qc.M_InOutLineConfirm_ID   AS ConfirmLineId,
                                  " + QualityCheckColumns("VA010_ShipConfParameters") + @",
                                  " + ConfirmationNoExpr("M_InOutLineConfirm") + @" AS ConfirmationNo,
                                  io.M_InOut_ID              AS DocRecordId,
                                  io.DocumentNo,
                                  COALESCE(io.IsSOTrx, 'N')  AS DocIsSOTrx,
                                  bp.Name                    AS BPartnerName
                           FROM VA010_ShipConfParameters qc
                           INNER JOIN M_InOutLineConfirm lc ON (lc.M_InOutLineConfirm_ID=qc.M_InOutLineConfirm_ID)
                           LEFT OUTER JOIN M_InOutLine iol ON (iol.M_InOutLine_ID=lc.M_InOutLine_ID)
                           LEFT OUTER JOIN M_InOut io ON (io.M_InOut_ID=iol.M_InOut_ID)
                           LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=io.C_BPartner_ID)
                           " + QualityCheckParamJoins("VA010_ShipConfParameters") + @"
                           WHERE qc.M_Product_ID=@M_Product_ID
                             AND qc.IsActive='Y'";
            // The check record is the main physical table here, so it carries the
            // access filter — and ORDER BY is appended after it, since the
            // rewriter adds its predicate to the end of what it is handed.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "qc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY " + QUALITY_CHECK_ORDER;

            return BuildLatestQualityCheck(
                Query(sql, ProductParam(M_Product_ID), "LoadLatestReceiptQualityCheck"),
                "RECEIPT", "M_InOut");
        }

        /// <summary>The latest check raised on an internal transfer's confirmation.</summary>
        private QualityCheckData LoadLatestMovementQualityCheck(Ctx ctx, int M_Product_ID)
        {
            if (!TableExists("VA010_MoveConfParameters")) return null;

            string sql = @"SELECT qc.M_MovementLineConfirm_ID AS ConfirmLineId,
                                  " + QualityCheckColumns("VA010_MoveConfParameters") + @",
                                  " + ConfirmationNoExpr("M_MovementLineConfirm") + @" AS ConfirmationNo,
                                  mv.M_Movement_ID            AS DocRecordId,
                                  mv.DocumentNo,
                                  'N'                         AS DocIsSOTrx,
                                  CAST(NULL AS VARCHAR(60))   AS BPartnerName
                           FROM VA010_MoveConfParameters qc
                           INNER JOIN M_MovementLineConfirm lc ON (lc.M_MovementLineConfirm_ID=qc.M_MovementLineConfirm_ID)
                           LEFT OUTER JOIN M_MovementLine mvl ON (mvl.M_MovementLine_ID=lc.M_MovementLine_ID)
                           LEFT OUTER JOIN M_Movement mv ON (mv.M_Movement_ID=mvl.M_Movement_ID)
                           " + QualityCheckParamJoins("VA010_MoveConfParameters") + @"
                           WHERE qc.M_Product_ID=@M_Product_ID
                             AND qc.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "qc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY " + QUALITY_CHECK_ORDER;

            return BuildLatestQualityCheck(
                Query(sql, ProductParam(M_Product_ID), "LoadLatestMovementQualityCheck"),
                "MOVEMENT", "M_Movement");
        }

        /// <summary>
        /// Newest check first. The parameter rows of ONE check share a
        /// confirmation line, so they arrive together whatever their own dates.
        /// </summary>
        private const string QUALITY_CHECK_ORDER =
            "CheckDate DESC, ConfirmLineId DESC, ParameterName";

        /// <summary>
        /// The columns a check row carries, on either confirmation table. Every
        /// one of them is optional between VA010 revisions, so each is probed and
        /// replaced by a typed NULL where the schema has not got it.
        /// </summary>
        private string QualityCheckColumns(string tableName)
        {
            string qaDate = ColumnExists(tableName, "VA010_QAQCDate")
                ? "COALESCE(qc.VA010_QAQCDate, qc.Created)" : "qc.Created";
            string actual = ColumnExists(tableName, "VA010_ActualValue")
                ? "qc.VA010_ActualValue" : "CAST(NULL AS VARCHAR(255))";
            string remark = ColumnExists(tableName, "Remark")
                ? "qc.Remark" : "CAST(NULL AS VARCHAR(255))";
            string qty = ColumnExists(tableName, "VA010_QuantityToVerify")
                ? "COALESCE(qc.VA010_QuantityToVerify, 0)" : "CAST(NULL AS NUMERIC)";

            return qaDate + @" AS CheckDate,
                   " + actual + @" AS ActualValue,
                   " + remark + @" AS Remark,
                   " + qty + @" AS QtyToVerify,
                   " + QualityCheckParamNameExpr(tableName) + @" AS ParameterName,
                   " + QualityCheckValueNameExpr(tableName) + " AS AcceptableValue";
        }

        /// <summary>
        /// The test-parameter and acceptable-value joins a check row is read
        /// through. A join is only made where the check table carries the key AND
        /// the referenced table names a display column — either missing costs the
        /// name, never the check.
        /// </summary>
        private string QualityCheckParamJoins(string tableName)
        {
            string joins = "";
            if (HasQualityCheckParamName(tableName))
            {
                joins += @" LEFT OUTER JOIN VA010_TestParameter tp
                              ON (tp.VA010_TestParameter_ID=qc.VA010_TestParameter_ID)";
            }
            if (HasQualityCheckValueName(tableName))
            {
                joins += @" LEFT OUTER JOIN VA010_TestPrmtrList tpl
                              ON (tpl.VA010_TestPrmtrList_ID=qc.VA010_TestPrmtrList_ID)";
            }
            return joins;
        }

        private bool HasQualityCheckParamName(string tableName)
        {
            return TableExists("VA010_TestParameter")
                && ColumnExists(tableName, "VA010_TestParameter_ID")
                && !string.IsNullOrEmpty(QualityCheckParamNameColumn());
        }

        private bool HasQualityCheckValueName(string tableName)
        {
            return TableExists("VA010_TestPrmtrList")
                && ColumnExists(tableName, "VA010_TestPrmtrList_ID")
                && !string.IsNullOrEmpty(QualityCheckValueNameColumn());
        }

        /// <summary>
        /// The confirmation line's own number where it keeps one. The receipt
        /// side does; the transfer side is not guaranteed to, and asking a table
        /// for a column it has not got would cost the whole check rather than
        /// just its number.
        /// </summary>
        private string ConfirmationNoExpr(string confirmLineTable)
        {
            return ColumnExists(confirmLineTable, "ConfirmationNo")
                ? "lc.ConfirmationNo" : "CAST(NULL AS VARCHAR(60))";
        }

        private string QualityCheckParamNameColumn()
        {
            return FindDisplayColumn("VA010_TestParameter",
                new string[] { "VA010_TestPrmtrName", "Name", "Description", "Value" });
        }

        private string QualityCheckValueNameColumn()
        {
            return FindDisplayColumn("VA010_TestPrmtrList",
                new string[] { "VA010_ParameterValue", "Name", "Description", "Value" });
        }

        private string QualityCheckParamNameExpr(string tableName)
        {
            return HasQualityCheckParamName(tableName)
                ? "tp." + QualityCheckParamNameColumn() : "CAST(NULL AS VARCHAR(255))";
        }

        private string QualityCheckValueNameExpr(string tableName)
        {
            return HasQualityCheckValueName(tableName)
                ? "tpl." + QualityCheckValueNameColumn() : "CAST(NULL AS VARCHAR(255))";
        }

        /// <summary>
        /// Turns the rows of a check query into ONE check: the newest confirmation
        /// line, with every parameter read on it. The rows arrive newest first, so
        /// the first row names the check and the rows sharing its confirmation
        /// line are its parameters.
        /// </summary>
        private QualityCheckData BuildLatestQualityCheck(DataSet ds, string source, string docTable)
        {
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;

            DataRow first = ds.Tables[0].Rows[0];
            int confirmLineId = Util.GetValueOfInt(first["ConfirmLineId"]);

            QualityCheckData check = new QualityCheckData();
            check.Source         = source;
            check.DocTableName   = docTable;
            check.DocRecordId    = Util.GetValueOfInt(first["DocRecordId"]);
            check.DocumentNo     = Util.GetValueOfString(first["DocumentNo"]);
            check.ConfirmationNo = Util.GetValueOfString(first["ConfirmationNo"]);
            check.BPartnerName   = Util.GetValueOfString(first["BPartnerName"]);
            check.DocIsSOTrx     = Util.GetValueOfString(first["DocIsSOTrx"]) == "Y";
            check.CheckDate      = Util.GetValueOfDateTime(first["CheckDate"]);
            check.QtyToVerify    = NullableDecimal(first["QtyToVerify"]);
            check.Lines          = new List<QualityCheckLineData>();
            check.IsComplete     = true;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                // Only the newest confirmation line is this check; the rows behind
                // it are earlier checks and belong to nobody on screen.
                if (Util.GetValueOfInt(r["ConfirmLineId"]) != confirmLineId) continue;

                string actual = Util.GetValueOfString(r["ActualValue"]);
                if (string.IsNullOrEmpty((actual ?? "").Trim())) check.IsComplete = false;

                check.Lines.Add(new QualityCheckLineData
                {
                    ParameterName   = Util.GetValueOfString(r["ParameterName"]),
                    AcceptableValue = Util.GetValueOfString(r["AcceptableValue"]),
                    ActualValue     = actual,
                    Remark          = Util.GetValueOfString(r["Remark"])
                });
            }
            return check.Lines.Count > 0 ? check : null;
        }

        // ----------------------------------------------------------------- //
        //  10. Supplier information                                          //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The vendors the product is bought from — the one it was LAST bought
        /// from first, then the alternatives — with the terms the vendor-product
        /// record stores and what the purchase history actually says.
        ///
        /// The history is read rather than trusted to M_Product_PO.PriceLastPO /
        /// PriceLastPODate: those two are only written by the processes that
        /// maintain them, so on a tenant whose orders do not update them the last
        /// used vendor showed no purchase at all. The most recent purchase ORDER
        /// per vendor answers it directly — its date, its document and the price
        /// on the line — and the stored fields stand in where a vendor has no
        /// order history to read.
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

            ApplyPurchaseHistory(ctx, M_Product_ID, rows);
            return rows;
        }

        /// <summary>
        /// Stamps each vendor row with what the product was LAST bought from it
        /// for — date, document and price off the most recent purchase order —
        /// and marks the single most recent of them as the last used vendor. The
        /// rest are alternatives.
        ///
        /// Where a vendor has no readable order history the stored PriceLastPO /
        /// PriceLastPODate stand in, so a row is never left blank when the
        /// vendor-product record does carry the figures.
        /// </summary>
        private void ApplyPurchaseHistory(Ctx ctx, int M_Product_ID, List<SupplierRowData> rows)
        {
            if (rows.Count == 0) return;

            // The latest purchase order per VENDOR, ranked outside the
            // access-filtered statement. Purchase orders proper only — a blanket
            // order or a vendor return is not what the product was bought on.
            string inner = @"SELECT o.C_BPartner_ID,
                                    o.C_Order_ID,
                                    o.DocumentNo,
                                    o.DateOrdered,
                                    ol.PriceActual,
                                    cur.CurSymbol,
                                    cur.ISO_Code,
                                    COALESCE(cur.StdPrecision, 2) AS CurPrecision
                             FROM C_Order o
                             INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID
                                                           AND ol.IsActive='Y')
                             LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=o.C_Currency_ID)"
                             + OrderDocTypeJoin(false) + @"
                             WHERE ol.M_Product_ID=@M_Product_ID
                               AND o.IsActive='Y'
                               AND o.IsSOTrx='N'"
                             + OrderRowPredicates(false) + @"
                               AND o.DocStatus IN ('CO','CL')";
            inner = MRole.GetDefault(ctx).AddAccessSQL(
                inner, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"SELECT x.C_BPartner_ID,
                                  x.C_Order_ID,
                                  x.DocumentNo,
                                  x.DateOrdered,
                                  x.PriceActual,
                                  x.CurSymbol,
                                  x.ISO_Code,
                                  x.CurPrecision
                           FROM (SELECT h.C_BPartner_ID,
                                        h.C_Order_ID,
                                        h.DocumentNo,
                                        h.DateOrdered,
                                        h.PriceActual,
                                        h.CurSymbol,
                                        h.ISO_Code,
                                        h.CurPrecision,
                                        ROW_NUMBER() OVER (PARTITION BY h.C_BPartner_ID
                                                           ORDER BY h.DateOrdered DESC,
                                                                    h.C_Order_ID DESC) AS Rn
                                 FROM (" + inner + @") h) x
                           WHERE x.Rn=1";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "ApplyPurchaseHistory");
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int vendorId = Util.GetValueOfInt(r["C_BPartner_ID"]);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].C_BPartner_ID != vendorId) continue;

                    rows[i].LastOrderDate  = Util.GetValueOfDateTime(r["DateOrdered"]);
                    rows[i].LastOrderNo    = Util.GetValueOfString(r["DocumentNo"]);
                    rows[i].LastOrderId    = Util.GetValueOfInt(r["C_Order_ID"]);
                    rows[i].LastOrderPrice = NullableDecimal(r["PriceActual"]);
                    // The order's own currency describes the price on it; the
                    // vendor-product row's currency describes only its own field.
                    string sym = Util.GetValueOfString(r["CurSymbol"]);
                    if (!string.IsNullOrEmpty(sym)) rows[i].CurSymbol = sym;
                    string iso = Util.GetValueOfString(r["ISO_Code"]);
                    if (!string.IsNullOrEmpty(iso)) rows[i].ISO_Code = iso;
                    rows[i].CurPrecision = Util.GetValueOfInt(r["CurPrecision"]);
                    break;
                }
            }

            // The most recently used vendor of them all. Only one row is marked:
            // "last used" is a superlative, not a category.
            int lastUsedIndex = -1;
            DateTime lastUsedOn = DateTime.MinValue;
            for (int i = 0; i < rows.Count; i++)
            {
                DateTime? when = rows[i].LastOrderDate ?? rows[i].PriceLastPODate;
                if (!when.HasValue) continue;
                if (lastUsedIndex < 0 || when.Value > lastUsedOn)
                {
                    lastUsedIndex = i;
                    lastUsedOn = when.Value;
                }
            }
            if (lastUsedIndex >= 0) rows[lastUsedIndex].IsLastUsed = true;

            // Last used first, then the alternatives in the order they came.
            if (lastUsedIndex > 0)
            {
                SupplierRowData lastUsed = rows[lastUsedIndex];
                rows.RemoveAt(lastUsedIndex);
                rows.Insert(0, lastUsed);
            }
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
        /// BOTH sides list ORDERS PROPER only. Each was every document of its
        /// direction the product appeared on, so a section labelled "Sales
        /// orders" carried quotations, proposals, blanket sales orders and
        /// customer RMAs as though they were sales orders — an offer commits
        /// nothing, a blanket order commits only a price, and an RMA sends stock
        /// back the other way. The document type behind the order decides it,
        /// with the order's own denormalised flags as the second guard; see
        /// <see cref="OrderDocTypeJoin"/> and <see cref="OrderRowPredicates"/>.
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

            // The quantity is the ENTERED one, which is stated in the line's own
            // unit — the unit this row names. QtyOrdered is the base-unit figure
            // and printing it beside "CARTONS" said 120 pieces were ordered in
            // cartons. The base figures are still read, unlabelled, because the
            // ordered-versus-delivered comparison has to be made in ONE unit.
            string inner = @"SELECT o.C_Order_ID,
                                    o.DocumentNo,
                                    o.DocStatus,
                                    o.DateOrdered,
                                    o.DatePromised,
                                    bp.Name AS BPartnerName,
                                    SUM(COALESCE(ol.QtyEntered, ol.QtyOrdered, 0)) AS Qty,
                                    SUM(COALESCE(ol.QtyOrdered, 0)) AS QtyOrderedBase,
                                    SUM(COALESCE(ol.QtyDelivered, 0)) AS QtyDeliveredBase,
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
                             LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=o.C_Currency_ID)"
                             + OrderDocTypeJoin(isSalesOrder) + @"
                             WHERE ol.M_Product_ID=@M_Product_ID
                               AND o.IsActive='Y'
                               AND o.IsSOTrx='" + soTrx + "'"
                             + OrderRowPredicates(isSalesOrder);
            inner = MRole.GetDefault(ctx).AddAccessSQL(
                inner, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            // The aggregation collapses a product that repeats down one order.
            // DatePromised is the ORDER's own, so grouping by it cannot split a
            // row — it is functionally dependent on the order id already.
            inner += @" GROUP BY o.C_Order_ID, o.DocumentNo, o.DocStatus, o.DateOrdered,
                                 o.DatePromised, bp.Name";

            // Latest N, ranked outside the access-filtered statement.
            string sql = @"SELECT x.C_Order_ID,
                                  x.DocumentNo,
                                  x.DocStatus,
                                  x.DateOrdered,
                                  x.DatePromised,
                                  x.BPartnerName,
                                  x.Qty,
                                  x.QtyOrderedBase,
                                  x.QtyDeliveredBase,
                                  x.LineNetAmt,
                                  x.UomName,
                                  x.CurSymbol,
                                  x.ISO_Code,
                                  x.CurPrecision
                           FROM (SELECT g.C_Order_ID,
                                        g.DocumentNo,
                                        g.DocStatus,
                                        g.DateOrdered,
                                        g.DatePromised,
                                        g.BPartnerName,
                                        g.Qty,
                                        g.QtyOrderedBase,
                                        g.QtyDeliveredBase,
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
                decimal ordered   = Util.GetValueOfDecimal(r["QtyOrderedBase"]);
                decimal delivered = Util.GetValueOfDecimal(r["QtyDeliveredBase"]);

                rows.Add(new OrderRowData
                {
                    C_Order_ID     = Util.GetValueOfInt(r["C_Order_ID"]),
                    DocumentNo     = Util.GetValueOfString(r["DocumentNo"]),
                    DocStatus      = Util.GetValueOfString(r["DocStatus"]),
                    DateOrdered    = Util.GetValueOfDateTime(r["DateOrdered"]),
                    DatePromised   = Util.GetValueOfDateTime(r["DatePromised"]),
                    BPartnerName   = Util.GetValueOfString(r["BPartnerName"]),
                    Qty            = Util.GetValueOfDecimal(r["Qty"]),
                    QtyOrderedBase = ordered,
                    QtyDeliveredBase = delivered,
                    FulfilStatus   = FulfilStatusOf(ordered, delivered),
                    LineNetAmt     = Util.GetValueOfDecimal(r["LineNetAmt"]),
                    UomName        = Util.GetValueOfString(r["UomName"]),
                    CurSymbol      = Util.GetValueOfString(r["CurSymbol"]),
                    ISO_Code       = Util.GetValueOfString(r["ISO_Code"]),
                    CurPrecision   = Util.GetValueOfInt(r["CurPrecision"]),
                    IsSOTrx        = isSalesOrder
                });
            }
            return rows;
        }

        /// <summary>
        /// How much of what was ordered has actually moved, decided in the ONE
        /// unit both figures are stated in (the base one):
        ///   DUE   - nothing has been received / delivered yet
        ///   SHORT - some of it has, but not all
        ///   FULL  - all of it has, or more
        /// An order with nothing ordered on this product is NONE, which the panel
        /// says nothing about.
        /// </summary>
        private static string FulfilStatusOf(decimal ordered, decimal delivered)
        {
            if (ordered <= 0) return "NONE";
            if (delivered <= 0) return "DUE";
            return delivered >= ordered ? "FULL" : "SHORT";
        }

        // ----------------------------------------------------------------- //
        //  13. Recent transactions (Item only)                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The product's physical movements — ALL of them, which is the set its
        /// Transaction tab lists — read from the platform's own
        /// material-transaction abstraction so shipments, receipts, inventory
        /// moves, internal use and production issues come from one place instead
        /// of four document tables stitched together. Nothing is filtered on the
        /// document behind a movement: a movement whose document this reader
        /// cannot name is still a movement of this product and is reported, under
        /// its movement type.
        ///
        /// Each row carries WHERE the movement happened (warehouse, locator) and
        /// which attribute set instance moved. Several movements can share one
        /// document — a receipt posts a row per line — and with only the document
        /// number on screen those rows were indistinguishable from one another.
        ///
        /// The money column is the COST the movement was booked at: the material
        /// cost detail recorded against the very line the transaction came from
        /// (M_CostDetail, cost element 0, primary accounting schema), stated in
        /// that schema's currency. It used to be the PRICE off the order line
        /// behind a receipt, which is what was agreed with a vendor rather than
        /// what the stock was valued at, and which no stock move has at all.
        /// A movement with no cost recorded shows nothing; no cost is computed
        /// here to fill the column.
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

            // Which SCREEN each movement's document lives on is decided from the
            // document itself — a receipt and a shipment are both M_InOut, and a
            // physical count and an internal use are both M_Inventory, so the
            // table alone cannot answer it. These three flags are what tell them
            // apart; the window is named from them in managed code.
            bool ioHasReturn      = ColumnExists("M_InOut", "IsReturnTrx");
            bool invHasInternalUse = ColumnExists("M_Inventory", "IsInternalUse");
            string ioReturnExpr      = ioHasReturn ? "COALESCE(io.IsReturnTrx, 'N')" : "'N'";
            string invInternalExpr   = invHasInternalUse ? "COALESCE(inv.IsInternalUse, 'N')" : "'N'";

            // The cost the movement was booked at, read against the very line the
            // transaction came from. A correlated SUM rather than a join: a line
            // can carry several cost-detail rows, and joining them would repeat
            // the transaction once per row.
            string unitCostExpr = BuildTransactionCostExpr(ctx);

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
                                    COALESCE(io.IsSOTrx, 'N') AS DocIsSOTrx,
                                    " + ioReturnExpr + @" AS DocIsReturn,
                                    " + invInternalExpr + @" AS DocIsInternalUse,
                                    wh.Name AS WarehouseName,
                                    COALESCE(loc.LocatorCombination, loc.Value) AS LocatorName,
                                    asi.Description AS AsiDescription,
                                    asi.Lot,
                                    asi.SerNo,
                                    asi.GuaranteeDate,
                                    " + unitCostExpr + @" AS UnitCost
                             FROM M_Transaction mt
                             LEFT OUTER JOIN M_Locator loc ON (loc.M_Locator_ID=mt.M_Locator_ID)
                             LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID=loc.M_Warehouse_ID)
                             LEFT OUTER JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID=mt.M_AttributeSetInstance_ID
                                                                            AND mt.M_AttributeSetInstance_ID > 0)
                             LEFT OUTER JOIN M_InOutLine iol ON (iol.M_InOutLine_ID=mt.M_InOutLine_ID)
                             LEFT OUTER JOIN M_InOut io ON (io.M_InOut_ID=iol.M_InOut_ID)
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

            // EVERY transaction the product has, newest first — the same set its
            // Transaction tab lists. It used to be the latest few, which left the
            // reader comparing a section headed "Recent transactions" against a
            // tab that had more in it and no way to reach the rest from here; the
            // section pages instead, so the whole history is on the panel.
            //
            // Every column the inner statement produces is carried through, so a
            // column added there does not have to be repeated in three places.
            string sql = @"SELECT t.*
                           FROM (" + inner + @") t
                           ORDER BY t.MovementDate DESC, t.M_Transaction_ID DESC";

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "LoadTransactions");
            if (ds == null || ds.Tables.Count == 0) return rows;

            // The cost is booked in the accounting schema's currency, not in a
            // document's — one currency for the whole column.
            AcctSchemaInfo schema = PrimaryAcctSchema(ctx);

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string docTable = Util.GetValueOfString(r["DocTableName"]);
                rows.Add(new TransactionRowData
                {
                    M_Transaction_ID = Util.GetValueOfInt(r["M_Transaction_ID"]),
                    MovementType     = Util.GetValueOfString(r["MovementType"]),
                    MovementDate     = Util.GetValueOfDateTime(r["MovementDate"]),
                    MovementQty      = Util.GetValueOfDecimal(r["MovementQty"]),
                    DocumentNo       = Util.GetValueOfString(r["DocumentNo"]),
                    DocTypeName      = Util.GetValueOfString(r["DocTypeName"]),
                    DocTableName     = docTable,
                    DocRecordId      = Util.GetValueOfInt(r["DocRecordId"]),
                    DocIsSOTrx       = Util.GetValueOfString(r["DocIsSOTrx"]) == "Y",
                    DocWindowName    = TransactionWindowName(
                        docTable,
                        Util.GetValueOfString(r["DocIsSOTrx"]) == "Y",
                        Util.GetValueOfString(r["DocIsReturn"]) == "Y",
                        Util.GetValueOfString(r["DocIsInternalUse"]) == "Y"),
                    WarehouseName    = Util.GetValueOfString(r["WarehouseName"]),
                    LocatorName      = Util.GetValueOfString(r["LocatorName"]),
                    Attributes       = BuildAsiText(
                        Util.GetValueOfString(r["AsiDescription"]),
                        Util.GetValueOfString(r["Lot"]),
                        Util.GetValueOfString(r["SerNo"]),
                        Util.GetValueOfDateTime(r["GuaranteeDate"])),
                    UnitCost         = NullableDecimal(r["UnitCost"]),
                    CurSymbol        = schema == null ? "" : schema.CurSymbol,
                    ISO_Code         = schema == null ? "" : schema.CurrencyISO,
                    CurPrecision     = schema == null ? 2  : schema.StdPrecision
                });
            }
            return rows;
        }

        /// <summary>
        /// How many material transactions the product has in total. The section
        /// now carries all of them, so this is what confirms that the rows on the
        /// panel are the whole history and not a window onto it. Counted under
        /// the same access filter the rows are read with.
        /// </summary>
        private int CountTransactions(Ctx ctx, int M_Product_ID)
        {
            if (!TableExists("M_Transaction")) return 0;

            string sql = @"SELECT COUNT(1) AS TxCount
                           FROM M_Transaction mt
                           WHERE mt.M_Product_ID=@M_Product_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "mt", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = Query(sql, ProductParam(M_Product_ID), "CountTransactions");
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return 0;
            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["TxCount"]);
        }

        /// <summary>
        /// The window a movement's document actually lives on. The TABLE cannot
        /// answer it — a receipt and a shipment are both M_InOut, and a physical
        /// count and an internal use are both M_Inventory — so the document's own
        /// flags decide, read from the line the transaction came from.
        ///
        /// The name is a candidate: the panel resolves it against the dictionary
        /// and falls back to the table's zoom target when the tenant has not got
        /// a window under that name, so an unknown name costs nothing.
        /// </summary>
        private static string TransactionWindowName(string docTable, bool isSOTrx,
                                                    bool isReturn, bool isInternalUse)
        {
            if (docTable == "M_InOut")
            {
                if (isSOTrx) return isReturn ? "VAS_CustomerReturn" : "VAS_DeliveryOrder";
                return isReturn ? "VAS_VendorReturn" : "VAS_MaterialReceipt";
            }
            if (docTable == "M_Inventory")
            {
                return isInternalUse ? "VAS_InternalUseInventory" : "VAS_PhysicalInventory";
            }
            if (docTable == "M_Movement") return "VAS_MaterialTransfer";
            return "";
        }

        /// <summary>
        /// The correlated expression that reads the material cost booked against
        /// the line a transaction came from, as an amount per unit. Cost element 0
        /// is the material cost; a schema without the cost table, or without the
        /// line columns to match on, degrades to a typed NULL and the column reads
        /// empty rather than the statement failing.
        /// </summary>
        private string BuildTransactionCostExpr(Ctx ctx)
        {
            if (!TableExists("M_CostDetail")) return "CAST(NULL AS NUMERIC)";

            AcctSchemaInfo schema = PrimaryAcctSchema(ctx);
            if (schema == null || schema.C_AcctSchema_ID <= 0) return "CAST(NULL AS NUMERIC)";

            // Which line columns the cost table actually carries. At least the
            // receipt line has to be there for the expression to say anything.
            List<string> matches = new List<string>();
            if (ColumnExists("M_CostDetail", "M_InOutLine_ID"))
            {
                matches.Add("(mt.M_InOutLine_ID IS NOT NULL AND cd.M_InOutLine_ID=mt.M_InOutLine_ID)");
            }
            if (ColumnExists("M_CostDetail", "M_InventoryLine_ID"))
            {
                matches.Add("(mt.M_InventoryLine_ID IS NOT NULL AND cd.M_InventoryLine_ID=mt.M_InventoryLine_ID)");
            }
            if (ColumnExists("M_CostDetail", "M_MovementLine_ID"))
            {
                matches.Add("(mt.M_MovementLine_ID IS NOT NULL AND cd.M_MovementLine_ID=mt.M_MovementLine_ID)");
            }
            if (matches.Count == 0) return "CAST(NULL AS NUMERIC)";

            // The schema id is inlined as an integer: the statement already
            // carries its one bind name, and positional binding needs each name
            // to occur exactly once.
            return @"(SELECT SUM(COALESCE(cd.Amt, 0)) / NULLIF(SUM(COALESCE(cd.Qty, 0)), 0)
                        FROM M_CostDetail cd
                       WHERE cd.M_Product_ID=mt.M_Product_ID
                         AND cd.IsActive='Y'
                         AND COALESCE(cd.M_CostElement_ID, 0)=0
                         AND cd.C_AcctSchema_ID=" + schema.C_AcctSchema_ID + @"
                         AND (" + string.Join(" OR ", matches.ToArray()) + "))";
        }

        /// <summary>
        /// The client's primary accounting schema, read once per request. Both the
        /// accounting section and the transaction cost need it.
        /// </summary>
        private AcctSchemaInfo PrimaryAcctSchema(Ctx ctx)
        {
            List<AcctSchemaInfo> schemas = LoadAcctSchemas(ctx);
            return schemas.Count > 0 ? schemas[0] : null;
        }

        // ----------------------------------------------------------------- //
        //  14. Accounting details                                            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Every account the product's own accounting tab (M_Product_Acct) holds,
        /// in this order. A column the schema has not got is skipped, and an
        /// account the product leaves unset is not reported at all.
        /// </summary>
        private static readonly string[] PRODUCT_ACCT_COLUMNS = new string[]
        {
            "P_Asset_Acct",
            "P_Revenue_Acct",
            "P_COGS_Acct",
            "P_PurchasePriceVariance_Acct",
            "P_Expense_Acct",
            "P_Resource_Absorption_Acct",
            "P_InvoicePriceVariance_Acct",
            "P_InventoryClearing_Acct",
            "P_CostAdjustment_Acct",
            "P_TradeDiscountRec_Acct",
            "P_TradeDiscountGrant_Acct",
            "P_MaterialOverhd_Acct"
        };

        /// <summary>
        /// The posting accounts set ON THE PRODUCT — exactly what its Accounting
        /// tab shows — read under the first of the client's accounting schemas
        /// that actually carries a row for it, the primary one first.
        ///
        /// Two things it deliberately no longer does:
        ///   - It does not fall back to the product CATEGORY's accounts. A
        ///     product whose own tab is empty was showing its category's numbers
        ///     as though they were its own, which is not what the tab says and is
        ///     not where the reader would go to change them.
        ///   - It does not report a fixed four accounts per product type. Any of
        ///     the accounts above that the product sets is reported, so an account
        ///     configured on the tab can no longer go missing from the panel.
        ///
        /// Service products are still excluded outright: no account of any kind
        /// is queried or shown for them.
        /// </summary>
        private AccountingData LoadAccounting(Ctx ctx, int M_Product_ID, string productType)
        {
            if (productType == TYPE_SERVICE) return null;

            List<string> wanted = new List<string>(PRODUCT_ACCT_COLUMNS);

            // The client's schemas, primary first. A tenant posting under a
            // second schema keeps its product accounts there, and reading only
            // the primary one reported that product as having none.
            List<AcctSchemaInfo> schemas = LoadAcctSchemas(ctx);
            for (int i = 0; i < schemas.Count; i++)
            {
                AcctSchemaInfo schema = schemas[i];
                Dictionary<string, int> productAcct = LoadAcctRow(ctx, "M_Product_Acct",
                    "M_Product_ID", M_Product_ID, schema.C_AcctSchema_ID, wanted);
                if (productAcct.Count == 0) continue;   // this schema has nothing; try the next

                // Only the accounts the product actually set reach here — the
                // reader returns a column solely when its combination id is real.
                List<int> combinationIds = new List<int>();
                foreach (string column in wanted)
                {
                    if (!productAcct.ContainsKey(column)) continue;
                    int id = productAcct[column];
                    if (!combinationIds.Contains(id)) combinationIds.Add(id);
                }

                Dictionary<int, ValidCombinationInfo> combos = LoadCombinations(ctx, combinationIds);

                AccountingData acct = new AccountingData();
                acct.SchemaName    = schema.Name;
                acct.CostingMethod = schema.CostingMethod;
                acct.CurrencyISO   = schema.CurrencyISO;
                acct.CurSymbol     = schema.CurSymbol;
                acct.Rows          = new List<AccountRowData>();

                // Reported in the order the list above declares them, not in
                // whatever order the dictionary happens to return.
                foreach (string column in wanted)
                {
                    if (!productAcct.ContainsKey(column)) continue;
                    int id = productAcct[column];

                    AccountRowData row = new AccountRowData();
                    row.AccountRole = column;
                    if (combos.ContainsKey(id))
                    {
                        row.Combination = combos[id].Combination;
                        row.Description = combos[id].Description;
                    }
                    acct.Rows.Add(row);
                }
                if (acct.Rows.Count > 0) return acct;
            }
            return null;   // nothing set on the product - no section
        }

        /// <summary>
        /// Reads the requested account columns from one accounting-defaults table
        /// for one owner record and accounting schema.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="tableName">The accounting-defaults table; M_Product_Acct.</param>
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
        /// The client's active accounting schemas with their costing method and
        /// currency, the PRIMARY one (AD_ClientInfo.C_AcctSchema1_ID) first —
        /// the same route the platform uses to reach the tenant's base currency.
        ///
        /// All of them, not just the primary: a tenant that posts under a second
        /// schema keeps its product accounts there, and a product read under the
        /// primary alone came back with none.
        /// </summary>
        private List<AcctSchemaInfo> LoadAcctSchemas(Ctx ctx)
        {
            // Read once per request: the accounting section walks the list, and
            // the transaction cost and its currency both ask for the primary one.
            if (_acctSchemas != null) return _acctSchemas;

            List<AcctSchemaInfo> schemas = new List<AcctSchemaInfo>();

            string sql = @"SELECT acs.C_AcctSchema_ID,
                                  acs.Name AS SchemaName,
                                  acs.CostingMethod,
                                  cur.ISO_Code,
                                  CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol
                                       ELSE cur.ISO_Code END AS CurrencySymbol,
                                  COALESCE(cur.StdPrecision, 2) AS StdPrecision
                           FROM AD_ClientInfo ci
                           INNER JOIN C_AcctSchema acs ON (acs.AD_Client_ID=ci.AD_Client_ID
                                                           AND acs.IsActive='Y')
                           INNER JOIN C_Currency cur ON (cur.C_Currency_ID=acs.C_Currency_ID)
                           WHERE ci.AD_Client_ID=@AD_Client_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            // Appended after the access filter. The CASE is repeated rather than
            // ordered by its alias, which not every database accepts.
            sql += @" ORDER BY CASE WHEN acs.C_AcctSchema_ID=ci.C_AcctSchema1_ID THEN 0
                                    ELSE 1 END, acs.C_AcctSchema_ID";

            DataSet ds = Query(sql,
                new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) },
                "LoadAcctSchemas");
            if (ds == null || ds.Tables.Count == 0) { _acctSchemas = schemas; return schemas; }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                schemas.Add(new AcctSchemaInfo
                {
                    C_AcctSchema_ID = Util.GetValueOfInt(r["C_AcctSchema_ID"]),
                    Name            = Util.GetValueOfString(r["SchemaName"]),
                    CostingMethod   = Util.GetValueOfString(r["CostingMethod"]),
                    CurrencyISO     = Util.GetValueOfString(r["ISO_Code"]),
                    CurSymbol       = Util.GetValueOfString(r["CurrencySymbol"]),
                    StdPrecision    = Util.GetValueOfInt(r["StdPrecision"])
                });
            }
            _acctSchemas = schemas;
            return schemas;
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
            // Calls (VA048_CallDetails). Appointments, tasks, letters and mails
            // are already read above by this panel's own loaders, so only the one
            // source it was missing is taken from the shared reader.
            LoadCallActivity(M_Product_ID, events);

            // Newest first, on the raw timestamp, with the entry's own identity as
            // the tie-break. List.Sort is NOT stable, so without it two events
            // stamped the same second could swap places between one read and the
            // next — and since the panel pages this list client-side, an entry
            // that moves across a page boundary reads as the same record showing
            // up twice. Formatting happens on the client.
            events.Sort(delegate (ActivityData a, ActivityData b)
            {
                int byDate = b.EventDate.GetValueOrDefault(DateTime.MinValue)
                              .CompareTo(a.EventDate.GetValueOrDefault(DateTime.MinValue));
                if (byDate != 0) return byDate;
                int byType = string.CompareOrdinal(a.Type ?? "", b.Type ?? "");
                return byType != 0 ? byType : b.Id.CompareTo(a.Id);
            });

            events = DeduplicateActivity(events);
            if (events.Count > MAX_ACTIVITY) events = events.GetRange(0, MAX_ACTIVITY);
            return events;
        }

        /// <summary>
        /// Drops entries the reader would see as the SAME event twice.
        ///
        /// Two things produce them. A source can hand back one record more than
        /// once — a chat entry reached through two chats on the record, a mail
        /// stored against it twice — and (type, id) settles those outright. And
        /// two DIFFERENT records can say exactly the same thing at the same
        /// moment: a note raised alongside the chat entry that quotes it, or the
        /// same field logged by two sources in one save. Nothing on screen tells
        /// those apart, so the second is dropped on what the reader actually
        /// sees — kind, headline, the move it states, its author and the second
        /// it happened in.
        ///
        /// The list arrives sorted, so the entry that survives is the first one
        /// in reading order and the feed's order is untouched.
        /// </summary>
        private static List<ActivityData> DeduplicateActivity(List<ActivityData> events)
        {
            List<ActivityData> kept = new List<ActivityData>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();

            foreach (ActivityData e in events)
            {
                string stamp = e.EventDate.HasValue
                    ? e.EventDate.Value.ToString("yyyyMMddHHmmss") : "";

                // Same record, read twice.
                string identity = (e.Type ?? "") + "#" + e.Id;
                // Same thing said twice, by two records. The body rides on the
                // key as well: two notes raised in one second by one person, both
                // with no subject of their own, are told apart by nothing else.
                string body = (e.Body ?? "");
                if (body.Length > 200) body = body.Substring(0, 200);
                string content = (e.Type ?? "") + "|" + (e.Title ?? "") + "|"
                               + (e.OldValue ?? "") + "|" + (e.NewValue ?? "") + "|"
                               + (e.Actor ?? "") + "|" + stamp + "|" + body;

                if (seen.ContainsKey(identity) || seen.ContainsKey(content)) continue;
                seen[identity] = true;
                seen[content]  = true;
                kept.Add(e);
            }
            return kept;
        }

        /// <summary>Field-level changes logged against the product (AD_ChangeLog).</summary>
        private void LoadFieldChangeActivity(Ctx ctx, int M_Product_ID, List<ActivityData> list)
        {
            // The column's REFERENCE travels with the row. AD_ChangeLog stores the
            // raw stored value, which for a coded column is an id or a code — so
            // "UOM changed from 100 to 105" and "Product Type from I to S" is what
            // the panel printed. Knowing the reference is what lets those be
            // resolved to the names the record screen shows (ResolveChangeValues).
            string sql = @"SELECT cl.AD_ChangeLog_ID,
                                  cl.OldValue,
                                  cl.NewValue,
                                  cl.Created,
                                  COALESCE(col.Name, col.ColumnName) AS FieldName,
                                  col.ColumnName            AS ColumnName,
                                  col.AD_Reference_ID       AS RefId,
                                  col.AD_Reference_Value_ID AS RefValueId,
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

            // One entry per FIELD per SAVE. The platform can log the same column
            // twice for a single save — a callout writing a value the user also
            // typed, or a re-save that re-states an unchanged field — and both rows
            // carry the same field, the same move and the same second, so the
            // timeline printed the identical line twice with nothing to tell them
            // apart. Keyed on exactly that: field, old, new and the second it
            // happened in.
            List<string> seen = new List<string>();

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string oldRaw = Util.GetValueOfString(r["OldValue"]);
                string newRaw = Util.GetValueOfString(r["NewValue"]);

                // A row that says nothing moved is not an edit. The platform logs
                // these when a save rewrites a field with the value it already had.
                if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal)) continue;

                DateTime? at = Util.GetValueOfDateTime(r["Created"]);
                string key = Util.GetValueOfString(r["ColumnName"]) + "|" + oldRaw + "|" + newRaw
                           + "|" + (at.HasValue ? at.Value.ToString("yyyyMMddHHmmss") : "");
                if (seen.Contains(key)) continue;
                seen.Add(key);

                string oldText, newText;
                ResolveChangeValues(ctx,
                    Util.GetValueOfString(r["ColumnName"]),
                    Util.GetValueOfInt(r["RefId"]),
                    Util.GetValueOfInt(r["RefValueId"]),
                    oldRaw, newRaw, out oldText, out newText);

                list.Add(new ActivityData
                {
                    Id        = Util.GetValueOfInt(r["AD_ChangeLog_ID"]),
                    Type      = "fieldupdate",
                    Title     = Util.GetValueOfString(r["FieldName"]),
                    OldValue  = oldText,
                    NewValue  = newText,
                    Actor     = Util.GetValueOfString(r["ActorName"]),
                    EventDate = at
                });
            }
        }

        /// <summary>
        /// Turns the RAW stored values of one change-log row into the text the
        /// record screen would show for them.
        ///
        /// AD_ChangeLog keeps what the column holds, not what it displays. For a
        /// coded column that is an id or a code, so the timeline read "UOM changed
        /// from 100 to 105" and "Product Type from I to S" — technically accurate
        /// and useless to a reader.
        ///
        /// Three kinds are resolved:
        ///   * DATE and DATE-TIME columns report the DATE alone. The log stores a
        ///     full timestamp whatever the field's type, so an edited date read
        ///     "20-08-2026 00:00:00" — a midnight nobody chose, against a field
        ///     with no time part at all.
        ///   * LIST columns (Product Type, and every other reference list) against
        ///     AD_Ref_List, in the reader's own language;
        ///   * TABLE / TABLEDIR columns — anything ending "_ID" — against the
        ///     referenced table's own identifier, taking the first of Name / Value
        ///     / DocumentNo that the table actually has.
        ///
        /// Anything that cannot be resolved is returned EXACTLY as stored. A raw
        /// id is a poor answer, but inventing one is worse, and a free-text column
        /// must never be rewritten.
        /// </summary>
        private void ResolveChangeValues(Ctx ctx, string columnName, int refId, int refValueId,
                                         string oldRaw, string newRaw,
                                         out string oldText, out string newText)
        {
            oldText = oldRaw;
            newText = newRaw;
            if (string.IsNullOrEmpty(columnName)) return;

            try
            {
                // ----- Date (15) / DateTime (16) -----
                // Shared with the other overview panels so every feed drops the
                // time the same way. Time (24) keeps its clock — that IS the value.
                if (refId == 15 || refId == 16)
                {
                    oldText = VAS_ChangeLogValueModel.DateOnly(oldRaw);
                    newText = VAS_ChangeLogValueModel.DateOnly(newRaw);
                    return;
                }

                // ----- Reference LIST (Product Type, Discontinued reason, ...) -----
                // A list column names the reference its values come from; without
                // one there is nothing to resolve against.
                if (refValueId > 0)
                {
                    Dictionary<string, string> labels = LoadRefListLabelsById(ctx, refValueId);
                    if (labels != null && labels.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(oldRaw) && labels.ContainsKey(oldRaw))
                            oldText = labels[oldRaw];
                        if (!string.IsNullOrEmpty(newRaw) && labels.ContainsKey(newRaw))
                            newText = labels[newRaw];
                        return;
                    }
                }

                // ----- Table reference (C_UOM_ID, M_Product_Category_ID, ...) -----
                if (!columnName.EndsWith("_ID", StringComparison.OrdinalIgnoreCase)) return;

                string table = columnName.Substring(0, columnName.Length - 3);
                string labelCol = FirstExistingColumn(table,
                    new string[] { "Name", "Value", "DocumentNo" });
                if (string.IsNullOrEmpty(labelCol)) return;

                oldText = LookupIdentifier(table, columnName, labelCol, oldRaw) ?? oldRaw;
                newText = LookupIdentifier(table, columnName, labelCol, newRaw) ?? newRaw;
            }
            catch (Exception ex)
            {
                // Never lose the entry over a lookup: the raw values still read.
                _log.Severe("ResolveChangeValues (" + columnName + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The identifier of one referenced row, or null when the value is not an
        /// id or names nothing. The id is parsed in managed code and inlined as an
        /// int — never bound — so a non-numeric stored value can neither reach the
        /// statement nor abort it on a numeric cast.
        /// </summary>
        private string LookupIdentifier(string table, string keyColumn, string labelColumn, string raw)
        {
            int id;
            if (string.IsNullOrEmpty(raw) || !int.TryParse(raw.Trim(), out id) || id <= 0)
                return null;
            try
            {
                string sql = "SELECT " + labelColumn + " FROM " + table +
                             " WHERE " + keyColumn + " = " + id;
                string name = Util.GetValueOfString(DB.ExecuteScalar(sql, null, null));
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception ex)
            {
                _log.Severe("LookupIdentifier (" + table + "." + labelColumn + "): " + ex.Message);
                return null;
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
        /// The labels of one reference list, keyed by stored value, addressed by
        /// the REFERENCE itself rather than by a table + column pair.
        ///
        /// The change log knows which reference a column draws on
        /// (AD_Column.AD_Reference_Value_ID) but has no use for the column's own
        /// name, so this is the form it needs — and it costs one lookup per
        /// reference rather than one per column.
        ///
        /// Resolved in the reader's own language, falling back to the untranslated
        /// name and then to the stored value itself.
        /// </summary>
        /// <param name="ctx">User context (language).</param>
        /// <param name="referenceId">AD_Column.AD_Reference_Value_ID.</param>
        /// <returns>Stored value to label; empty when the reference names nothing.</returns>
        private Dictionary<string, string> LoadRefListLabelsById(Ctx ctx, int referenceId)
        {
            Dictionary<string, string> labels;
            if (_refListCache.TryGetValue(referenceId, out labels)) return labels;

            labels = new Dictionary<string, string>();
            try
            {
                // Two binds, each occurring once, in the order they appear.
                string sql = @"SELECT rl.Value,
                                      COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                               FROM AD_Ref_List rl
                               LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID
                                                                       AND rlt.AD_Language=@AD_Language
                                                                       AND rlt.IsActive='Y')
                               WHERE rl.AD_Reference_ID=@AD_Reference_ID
                                 AND rl.IsActive='Y'";

                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@AD_Language", ctx == null ? "en_US" : ctx.GetAD_Language()),
                    new SqlParameter("@AD_Reference_ID", referenceId)
                };

                DataSet ds = Query(sql, param, "LoadRefListLabelsById(" + referenceId + ")");
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        string code = Util.GetValueOfString(r["Value"]);
                        if (!string.IsNullOrEmpty(code) && !labels.ContainsKey(code))
                            labels[code] = Util.GetValueOfString(r["DisplayName"]);
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: without labels the panel shows the stored code, which
                // is still better than showing nothing.
                _log.Severe("VAS_190 LoadRefListLabelsById(" + referenceId + "): " + ex.Message);
            }

            _refListCache[referenceId] = labels;
            return labels;
        }

        /// <summary>Reference labels already resolved on this request. A product's
        /// change log revisits the same few references over and over.</summary>
        private readonly Dictionary<int, Dictionary<string, string>> _refListCache =
            new Dictionary<int, Dictionary<string, string>>();

        /// <summary>
        /// The first of the candidate columns that exists on the table, or an empty
        /// string when it has none of them. Used to pick a referenced table's
        /// identifier without assuming every table names it the same way.
        /// </summary>
        private string FirstExistingColumn(string tableName, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i])) return candidates[i];
            }
            return "";
        }

        /// <summary>
        /// Mails sent to or received against the product. The body travels with
        /// the row so the panel can reveal it inline without a second round trip;
        /// the attachment BLOB is never selected.
        /// </summary>
        /// <summary>Reads the shared activity sources (VAS_ActivitySourcesModel).
        /// This panel has its own appointment, mail and chat loaders, so only the
        /// CALLS are taken from it.</summary>
        private readonly VAS_ActivitySourcesModel _activitySources = new VAS_ActivitySourcesModel();

        /// <summary>
        /// Calls logged against the product (VA048_CallDetails, pinned by
        /// AD_Table_ID + Record_ID). Every call lives in that one table — there is
        /// no type to distinguish, so no filter beyond the record link — and the
        /// shared reader skips the whole source where the VA048 module is not
        /// installed.
        /// </summary>
        private void LoadCallActivity(int M_Product_ID, List<ActivityData> list)
        {
            List<VAS_ActivitySourceRow> rows =
                _activitySources.Load("M_Product", M_Product_ID, false);
            foreach (VAS_ActivitySourceRow s in rows)
            {
                // Only the calls: the other kinds this panel reads for itself, in
                // richer form, and taking them here too would double every row.
                if (s.Kind != "call") continue;

                ActivityData a = new ActivityData();
                a.Id        = s.RecordId;
                a.Type      = "call";
                a.Title     = s.Title;
                a.Body      = s.Body;
                a.MailTo    = s.MailTo;      // the number it reached
                a.Actor     = s.ActorName;
                a.EventDate = s.EventTime;
                list.Add(a);
            }
        }

        private void LoadMailActivity(Ctx ctx, int M_Product_ID, List<ActivityData> list)
        {
            if (!TableExists("MailAttachment1")) return;

            string sql = @"SELECT ma.MailAttachment1_ID,
                                  ma.AttachmentType,
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
                // MailAttachment1 holds two kinds of correspondence, and
                // AttachmentType is what separates them: 'I' is an inbound LETTER,
                // anything else a mail. They used to arrive as one type, so a
                // letter was reported to the reader as an e-mail.
                //
                // The test is for 'I' rather than for 'M' deliberately: the value
                // varies between installations and some leave it null, so asking
                // for 'M' would hide mails that are really there. Read this way the
                // two kinds partition the table — nothing hidden, nothing counted
                // twice.
                a.Type      = Util.GetValueOfString(r["AttachmentType"]) == "I"
                                  ? "letter" : "mail";
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
            // One answer per column per request. The accounting section asks
            // about a dozen of them, once for each accounting schema it tries,
            // and the dictionary cannot change while a panel is being built.
            string cacheKey = tableName.ToUpper() + "." + columnName.ToUpper();
            if (_columnExists.ContainsKey(cacheKey)) return _columnExists[cacheKey];

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
                bool exists = Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
                _columnExists[cacheKey] = exists;
                return exists;
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
            public string Name            { get; set; }
            public string CostingMethod   { get; set; }
            public string CurrencyISO     { get; set; }
            public string CurSymbol       { get; set; }
            public int    StdPrecision    { get; set; }
        }

        /// <summary>
        /// What one direction's OPEN orders add up to: the quantity still to move,
        /// how many orders it came from, and — only where that is exactly one —
        /// that order's ordered and promised dates.
        /// </summary>
        private class OutstandingOrderInfo
        {
            public decimal   Qty          { get; set; }
            public int       OrderCount   { get; set; }
            public DateTime? DateOrdered  { get; set; }
            public DateTime? DatePromised { get; set; }
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
            /// <summary>M_Product.GuaranteeDays — the shelf life kept on the
            /// product's own stock tab; 0 when none is set there.</summary>
            public int       GuaranteeDays         { get; set; }
        }

        /// <summary>One attribute-set control or instance attribute.</summary>
        public class AttributeRowData
        {
            public string AttributeSetName { get; set; }
            /// <summary>The set's MandatoryType code (N / A / S); the panel labels it.</summary>
            public string SetMandatoryType { get; set; }
            public string Name             { get; set; }   // control key or attribute name
            public string Kind             { get; set; }   // control | instance
            public string ChipKey          { get; set; }   // MANDATORY | OPTIONAL
            public string ValueType        { get; set; }
            public int    ValueCount       { get; set; }
            /// <summary>Shelf life in days behind an expiry control: the product's
            /// own GuaranteeDays, else the attribute set's.</summary>
            public int    GuaranteeDays    { get; set; }
            /// <summary>The lot / serial number control record the set names, on
            /// those two control rows. Empty when the set names none.</summary>
            public string ControlName      { get; set; }
            /// <summary>Instance attribute (M_Attribute.IsInstanceAttribute): the
            /// value is captured per attribute-set INSTANCE rather than being a
            /// fixed property of the product.</summary>
            public bool   IsInstanceAttribute { get; set; }
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
            /// <summary>How many OPEN sales orders the reserved figure came from.</summary>
            public int     ReservedOrderCount { get; set; }
            /// <summary>How many OPEN purchase orders the on-order figure came from.</summary>
            public int     OnOrderCount       { get; set; }
            /// <summary>Ordered / promised dates of the ONE open sales order behind
            /// the reserved figure. Null unless the count is exactly one — with
            /// several orders in play neither date describes the figure.</summary>
            public DateTime? ReservedDateOrdered  { get; set; }
            public DateTime? ReservedDatePromised { get; set; }
            /// <summary>The same two dates for the one open purchase order.</summary>
            public DateTime? OnOrderDateOrdered   { get; set; }
            public DateTime? OnOrderDatePromised  { get; set; }
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
            /// <summary>This unit is the product's default PURCHASE uom
            /// (M_Product.VAS_PurchaseUOM_ID) — the unit its purchase documents
            /// are keyed in. False on a schema without the column.</summary>
            public bool    IsPurchaseUom     { get; set; }
            /// <summary>This unit is the product's default SALES uom
            /// (M_Product.VAS_SalesUOM_ID).</summary>
            public bool    IsSalesUom        { get; set; }
            /// <summary>This unit is the product's default CONSUMABLE uom
            /// (M_Product.VAS_ConsumableUOM_ID) — the unit it is issued in.</summary>
            public bool    IsConsumableUom   { get; set; }
        }

        public class PricingRowData
        {
            public int       M_PriceList_ID { get; set; }
            /// <summary>The VERSION this price is on. A list carries several, and
            /// the product can be priced on more than one of them.</summary>
            public int       M_PriceList_Version_ID { get; set; }
            /// <summary>True on the version of this list that is in force on the
            /// business date — the latest one whose ValidFrom has been reached.
            /// Every other version of the list is history or is not yet in
            /// force, and is reported as such rather than being left out.</summary>
            public bool      IsCurrentVersion { get; set; }
            public string    PriceListName  { get; set; }
            public string    VersionName    { get; set; }
            public DateTime? ValidFrom      { get; set; }
            public string    ISO_Code       { get; set; }
            public string    CurSymbol      { get; set; }
            public int       CurPrecision   { get; set; }
            public decimal   PriceList      { get; set; }
            public decimal   PriceStd       { get; set; }
            public decimal   PriceLimit     { get; set; }
            /// <summary>The unit this price is stated in (M_ProductPrice.C_UOM_ID);
            /// empty on a schema without the column.</summary>
            public string    UomName        { get; set; }
            /// <summary>The attribute set instance the price is for, read the same
            /// way the stock rows read theirs. Empty when the price is not
            /// attribute-specific.</summary>
            public string    Attributes     { get; set; }
        }

        public class BomRowData
        {
            public string  Kind            { get; set; }   // own | usedin
            public int     M_BOM_ID        { get; set; }
            /// <summary>The id of the record this row IS — the BOM on an own row,
            /// the BOM detail line on a where-used one. It is the ordering
            /// tie-break, never a navigation target.</summary>
            public int     RecordId        { get; set; }
            public string  Name            { get; set; }
            public string  Description     { get; set; }
            public int     ComponentCount  { get; set; }   // own
            public int     ParentProductId { get; set; }   // usedin
            public decimal QtyPerParent    { get; set; }   // usedin
            /// <summary>The attribute set instance the DETAIL LINE is specified
            /// for (M_BOMProduct.M_AttributeSetInstance_ID). Empty on an own BOM
            /// row and where the line names no instance.</summary>
            public string  Attributes      { get; set; }   // usedin
            /// <summary>When the record was created — the BOM itself on an own
            /// row, the detail line on a where-used one. The section orders on
            /// it, newest first.</summary>
            public DateTime? Created       { get; set; }
            public bool    IsVerified      { get; set; }
        }

        public class QualityParamRowData
        {
            public string   PlanName             { get; set; }
            public string   PlanDescription      { get; set; }
            public int      LineNo               { get; set; }
            public string   ParameterName        { get; set; }
            /// <summary>The TEST parameter's own description
            /// (VA010_TestParameter.Description) — what the test is.</summary>
            public string   ParameterDescription { get; set; }
            public string   Observation          { get; set; }
            public string   ParameterType        { get; set; }
            public decimal? Weightage            { get; set; }
            public string   ListValue            { get; set; }
            /// <summary>The description this plan gives the parameter
            /// (VA010_AssgndParameters.Description) — what it is checked for HERE.
            /// It does not replace <see cref="ParameterDescription"/>: the two
            /// answer different questions and the panel shows both.</summary>
            public string   AssignedDescription  { get; set; }
        }

        /// <summary>
        /// The latest quality CHECK recorded against the product: the
        /// confirmation it was raised on, and every parameter read on it.
        /// </summary>
        public class QualityCheckData
        {
            /// <summary>RECEIPT (a shipment / receipt confirmation) or MOVEMENT
            /// (an internal-transfer confirmation) — which confirmation the check
            /// was raised on.</summary>
            public string    Source        { get; set; }
            public string    DocumentNo    { get; set; }
            public string    ConfirmationNo { get; set; }
            public string    BPartnerName  { get; set; }
            /// <summary>When it was checked: VA010_QAQCDate, falling back to when
            /// the check record itself was created.</summary>
            public DateTime? CheckDate     { get; set; }
            public decimal?  QtyToVerify   { get; set; }
            /// <summary>Every parameter read on this one check has a result: the
            /// check is complete. False while any is still blank.</summary>
            public bool      IsComplete    { get; set; }
            /// <summary>The document the check hangs off, for the panel's zoom.</summary>
            public string    DocTableName  { get; set; }
            public int       DocRecordId   { get; set; }
            public bool      DocIsSOTrx    { get; set; }
            public List<QualityCheckLineData> Lines { get; set; }
        }

        /// <summary>One parameter read on a quality check.</summary>
        public class QualityCheckLineData
        {
            public string ParameterName   { get; set; }
            /// <summary>The value the plan expects (VA010_TestPrmtrList).</summary>
            public string AcceptableValue { get; set; }
            /// <summary>What was actually read. Empty while the parameter is
            /// still to be checked.</summary>
            public string ActualValue     { get; set; }
            public string Remark          { get; set; }
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
            /// <summary>True on the ONE vendor the product was most recently
            /// bought from. Every other vendor is an alternative.</summary>
            public bool      IsLastUsed           { get; set; }
            /// <summary>The most recent purchase order from this vendor for this
            /// product — read from the orders themselves, not from the stored
            /// PriceLastPO fields. Null where the vendor has no order history.</summary>
            public DateTime? LastOrderDate        { get; set; }
            public string    LastOrderNo          { get; set; }
            public int       LastOrderId          { get; set; }
            public decimal?  LastOrderPrice       { get; set; }
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
            /// <summary>The order's own promised date. The panel states it on a
            /// row that still has something to move — when it is due is what the
            /// reader acts on.</summary>
            public DateTime? DatePromised { get; set; }
            public string    BPartnerName { get; set; }
            /// <summary>Quantity in the LINE's own unit (QtyEntered), which is the
            /// unit <see cref="UomName"/> names.</summary>
            public decimal   Qty          { get; set; }
            /// <summary>Ordered and delivered in the product's BASE unit — the one
            /// unit both are stated in, which is what makes them comparable.</summary>
            public decimal   QtyOrderedBase   { get; set; }
            public decimal   QtyDeliveredBase { get; set; }
            /// <summary>DUE | SHORT | FULL | NONE — see FulfilStatusOf.</summary>
            public string    FulfilStatus     { get; set; }
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
            /// <summary>The document's own direction, which is what picks the sales
            /// or purchase window for a table that serves both.</summary>
            public bool      DocIsSOTrx       { get; set; }
            /// <summary>The window the document actually lives on, named from its
            /// own flags — see TransactionWindowName. Empty when unresolved, and
            /// the panel then falls back to the table's zoom target.</summary>
            public string    DocWindowName    { get; set; }
            /// <summary>Where the movement happened. Two movements can share one
            /// document, and these are what tell their rows apart.</summary>
            public string    WarehouseName    { get; set; }
            public string    LocatorName      { get; set; }
            public string    Attributes       { get; set; }
            /// <summary>The material cost the movement was booked at, per unit, in
            /// the primary accounting schema's currency. Null where none is
            /// recorded against the line.</summary>
            public decimal?  UnitCost         { get; set; }
            public string    CurSymbol        { get; set; }
            public string    ISO_Code         { get; set; }
            public int       CurPrecision     { get; set; }
        }

        public class AccountRowData
        {
            public string AccountRole    { get; set; }   // the M_Product_Acct column name
            public string Combination    { get; set; }
            public string Description    { get; set; }
        }

        public class AccountingData
        {
            /// <summary>The accounting schema these accounts were read under.</summary>
            public string                SchemaName    { get; set; }
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
            /// <summary>The latest quality CHECK carried out on the product —
            /// what was found, beside the plan's statement of what is checked.
            /// Null when the product has never been checked.</summary>
            public QualityCheckData            QualityCheck   { get; set; }
            public List<SupplierRowData>       Suppliers      { get; set; }
            public List<OrderRowData>          SalesOrders    { get; set; }
            public List<OrderRowData>          PurchaseOrders { get; set; }
            public List<TransactionRowData>    Transactions   { get; set; }
            /// <summary>How many material transactions the product has in ALL.
            /// <see cref="Transactions"/> carries all of them, so this is what
            /// confirms the section is the whole history.</summary>
            public int                         TransactionTotal { get; set; }
            public AccountingData              Accounting     { get; set; }
            public List<ActivityData>          Activity       { get; set; }
        }
    }
}
