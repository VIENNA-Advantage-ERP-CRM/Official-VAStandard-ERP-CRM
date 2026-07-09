/******************************************************
 * Module Name    : VAS
 * Purpose        : Overall Inventory product search widget endpoints
 * chronological  : Development
 * Created Date   : 2026-06-21
 * Created by     : VAI154
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_078_ProductSearchWidget
    /// Purpose     : Provides secured product suggestions and product-detail
    ///               data to the Overall Inventory Product Search widget.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    /// </summary>
    public class VAS_078_ProductSearchWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_078_ProductSearchWidgetController).FullName);

        // Review #36: M_Transaction.IsReversed exists on some deployments (the
        // Oracle test DB) but not on others (the PostgreSQL one); referencing a
        // missing column fails the movements query. Cached per app lifetime.
        private static bool? _transactionHasIsReversed;

        /// <summary>True when the application dictionary has M_Transaction.IsReversed.</summary>
        private bool TransactionHasIsReversed()
        {
            if (_transactionHasIsReversed.HasValue) { return _transactionHasIsReversed.Value; }

            string sql = @"
                SELECT COUNT(1)
                FROM AD_Column ColumnInfo
                INNER JOIN AD_Table TableInfo ON (TableInfo.AD_Table_ID=ColumnInfo.AD_Table_ID AND TableInfo.IsActive='Y')
                WHERE ColumnInfo.IsActive='Y'
                  AND UPPER(TableInfo.TableName)='M_TRANSACTION'
                  AND UPPER(ColumnInfo.ColumnName)='ISREVERSED'";

            _transactionHasIsReversed = Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[0], null)) > 0;
            return _transactionHasIsReversed.Value;
        }

        /// <summary>
        /// Searches products for the type-ahead suggestion dropdown.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult SearchProducts(string q, int max = 7)
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(SearchProductsData(ctx, q, max));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_078_ProductSearchWidget.SearchProducts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Loads the selected product's overview, stock, orders, movements, and requisitions.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductDetail(int M_Product_ID, int pageSize = 5)
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            if (pageSize <= 0) { pageSize = 5; }
            if (pageSize > 50) { pageSize = 50; }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetProductDetailData(ctx, M_Product_ID, pageSize));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_078_ProductSearchWidget.GetProductDetail", ex);
                return ErrorResult(ctx);
            }
        }

        private ProductSearchResult SearchProductsData(Ctx ctx, string searchText, int maxRows)
        {
            ProductSearchResult result = new ProductSearchResult
            {
                Rows = new List<ProductSearchRow>()
            };

            if (ctx == null) { return result; }

            SchemaCurrency currency = GetSchemaCurrency(ctx);
            result.CurrencySymbol = currency.Symbol;
            result.CurrencyIso = currency.IsoCode;
            result.StdPrecision = currency.StdPrecision;

            searchText = (searchText ?? "").Trim();
            if (searchText.Length == 0) { return result; }

            if (maxRows <= 0 || maxRows > 7) { maxRows = 7; }

            int clientId = ctx.GetAD_Client_ID();
            int orgId = ctx.GetAD_Org_ID();
            string likeValue = "%" + searchText.ToUpperInvariant() + "%";

            string searchProductsSql = @"
                SELECT Product.M_Product_ID,
                       Product.Name AS Product_Name,
                       Product.Value AS Product_Code,
                       Product.SKU,
                       Product.UPC,
                       Product.ProductType AS Product_Type,
                       ProductCategory.Name AS Category_Name
                FROM M_Product Product
                LEFT OUTER JOIN M_Product_Category ProductCategory ON (ProductCategory.M_Product_Category_ID=Product.M_Product_Category_ID AND ProductCategory.IsActive=N'Y')
                WHERE Product.IsActive=N'Y'
                  AND Product.AD_Client_ID=@Product_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@Product_Org_ID,0),Product.AD_Org_ID))
                  AND (
                      UPPER(COALESCE(Product.Name,N'')) LIKE @Product_Name
                      OR UPPER(COALESCE(Product.Value,N'')) LIKE @Product_Code
                      OR UPPER(COALESCE(Product.SKU,N'')) LIKE @Product_SKU
                      OR UPPER(COALESCE(Product.UPC,N'')) LIKE @Product_UPC
                      OR UPPER(COALESCE(Product.ProductType,'')) LIKE @Product_Type
                      OR UPPER(COALESCE(ProductCategory.Name,N'')) LIKE @Product_Category
                  )";

            searchProductsSql = AddAccessSql(ctx, searchProductsSql, "Product");
            searchProductsSql += @"
                ORDER BY Product.Name
                OFFSET 0 ROWS FETCH NEXT @Max_Rows ROWS ONLY";

            string costRowsSql = BuildCostRowsSql(ctx, "Cost.M_Product_ID IN (SELECT SearchProduct.M_Product_ID FROM SearchProducts SearchProduct)");

            string storageRowsSql = @"
                SELECT Storage.M_Product_ID,
                       COALESCE(Storage.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       SUM(COALESCE(Storage.QtyOnHand,0)) AS Qty_On_Hand
                FROM M_Storage Storage
                INNER JOIN M_Locator Locator ON (Locator.M_Locator_ID=Storage.M_Locator_ID AND Locator.IsActive=N'Y')
                WHERE Storage.IsActive=N'Y'
                  AND Storage.AD_Client_ID=@Storage_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@Storage_Org_ID,0),Storage.AD_Org_ID))";

            storageRowsSql = AddAccessSql(ctx, storageRowsSql, "Storage");
            storageRowsSql += @"
                  AND Storage.M_Product_ID IN (SELECT SearchProduct.M_Product_ID FROM SearchProducts SearchProduct)
                GROUP BY Storage.M_Product_ID,
                         COALESCE(Storage.M_AttributeSetInstance_ID,0)";

            string stockValuesSql = @"
                SELECT StorageRows.M_Product_ID,
                       SUM(StorageRows.Qty_On_Hand) AS On_Hand_Qty,
                       SUM(StorageRows.Qty_On_Hand * COALESCE(AttributeCost.Unit_Cost,ProductCost.Unit_Cost,0)) AS Stock_Value
                FROM StorageRows
                LEFT OUTER JOIN AttributeCost ON (AttributeCost.M_Product_ID=StorageRows.M_Product_ID AND AttributeCost.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN ProductCost ON (ProductCost.M_Product_ID=StorageRows.M_Product_ID)
                GROUP BY StorageRows.M_Product_ID";

            string sql = @"
                WITH SearchProducts AS (
                    " + searchProductsSql + @"
                ),
                CostRows AS (
                    " + costRowsSql + @"
                ),
                AttributeCost AS (
                    " + AttributeCostSql + @"
                ),
                ProductCost AS (
                    " + ProductCostSql + @"
                ),
                StorageRows AS (
                    " + storageRowsSql + @"
                ),
                StockValues AS (
                    " + stockValuesSql + @"
                )
                SELECT SearchProducts.M_Product_ID,
                       SearchProducts.Product_Name,
                       SearchProducts.Product_Code,
                       SearchProducts.SKU,
                       SearchProducts.UPC,
                       SearchProducts.Product_Type,
                       SearchProducts.Category_Name,
                       COALESCE(StockValues.On_Hand_Qty,0) AS On_Hand_Qty,
                       COALESCE(StockValues.Stock_Value,0) AS Stock_Value
                FROM SearchProducts
                LEFT OUTER JOIN StockValues ON (StockValues.M_Product_ID=SearchProducts.M_Product_ID)
                ORDER BY SearchProducts.Product_Name";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Product_Client_ID", clientId),
                new SqlParameter("@Product_Org_ID", orgId),
                new SqlParameter("@Product_Name", likeValue),
                new SqlParameter("@Product_Code", likeValue),
                new SqlParameter("@Product_SKU", likeValue),
                new SqlParameter("@Product_UPC", likeValue),
                new SqlParameter("@Product_Type", SqlDbType.VarChar) { Value = likeValue },
                new SqlParameter("@Product_Category", likeValue),
                new SqlParameter("@Max_Rows", maxRows),
                new SqlParameter("@C_AcctSchema_ID", currency.AcctSchemaId),
                new SqlParameter("@Cost_Client_ID", clientId),
                new SqlParameter("@Element_Client_ID1", clientId),
                new SqlParameter("@Element_Client_ID2", clientId),
                new SqlParameter("@Storage_Client_ID", clientId),
                new SqlParameter("@Storage_Org_ID", orgId)
            };

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    result.Rows.Add(new ProductSearchRow
                    {
                        ProductId = Util.GetValueOfInt(dr["M_Product_ID"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        ProductCode = Util.GetValueOfString(dr["Product_Code"]),
                        SKU = Util.GetValueOfString(dr["SKU"]),
                        UPC = Util.GetValueOfString(dr["UPC"]),
                        ProductType = Util.GetValueOfString(dr["Product_Type"]),
                        CategoryName = Util.GetValueOfString(dr["Category_Name"]),
                        OnHandQty = Util.GetValueOfDecimal(dr["On_Hand_Qty"]),
                        StockValue = Util.GetValueOfDecimal(dr["Stock_Value"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private ProductDetail GetProductDetailData(Ctx ctx, int productId, int pageSize)
        {
            if (ctx == null || productId <= 0) { return null; }

            ProductOverview overview = GetOverview(ctx, productId);
            if (overview == null) { return null; }

            SchemaCurrency currency = GetSchemaCurrency(ctx);

            ProductDetail detail = new ProductDetail
            {
                Overview = overview,
                Stock = GetStockRows(ctx, productId, currency),
                PurchaseOrders = GetOrders(ctx, productId, false, pageSize),
                SalesOrders = GetOrders(ctx, productId, true, pageSize),
                Movements = GetMovements(ctx, productId, pageSize),
                Requisitions = GetRequisitions(ctx, productId, pageSize),
                ReorderPoint = GetReorderPoint(ctx, productId),
                PreferredSupplier = GetPreferredSupplier(ctx, productId),
                // Review #6: every product reads Active unless it is flagged Discontinued.
                Status = overview.Discontinued ? "D" : "Y",
                CurrencySymbol = currency.Symbol,
                CurrencyIso = currency.IsoCode,
                StdPrecision = currency.StdPrecision
            };

            foreach (ProductStockRow stockRow in detail.Stock)
            {
                detail.OnHandQty += stockRow.Quantity;
                detail.StockValue += stockRow.StockValue;
            }

            return detail;
        }

        private ProductOverview GetOverview(Ctx ctx, int productId)
        {
            string sql = @"
                SELECT Product.M_Product_ID,
                       Product.Name AS Product_Name,
                       Product.Value AS Product_Code,
                       Product.SKU,
                       Product.UPC,
                       Product.ProductType AS Product_Type,
                       ProductCategory.Name AS Category_Name,
                       UOM.Name AS UOM_Name,
                       Product.IsActive,
                       Product.Discontinued,
                       Product.DiscontinuedBy,
                       Product.AD_Image_ID,
                       ProductImage.ImageExtension,
                       ProductImage.ImageURL AS Image_URL
                FROM M_Product Product
                LEFT OUTER JOIN M_Product_Category ProductCategory ON (ProductCategory.M_Product_Category_ID=Product.M_Product_Category_ID AND ProductCategory.IsActive=N'Y')
                LEFT OUTER JOIN C_UOM UOM ON (UOM.C_UOM_ID=Product.C_UOM_ID AND UOM.IsActive=N'Y')
                LEFT OUTER JOIN AD_Image ProductImage ON (ProductImage.AD_Image_ID=Product.AD_Image_ID AND ProductImage.IsActive=N'Y')
                WHERE Product.M_Product_ID=@M_Product_ID
                  AND Product.IsActive=N'Y'
                  AND Product.AD_Client_ID=@AD_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),Product.AD_Org_ID))";

            sql = AddAccessSql(ctx, sql, "Product");

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID())
            };

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                if (dr == null || !dr.Read()) { return null; }

                int adImageId = Util.GetValueOfInt(dr["AD_Image_ID"]);
                string imageExtension = Util.GetValueOfString(dr["ImageExtension"]);
                string storedImageUrl = Util.GetValueOfString(dr["Image_URL"]);

                return new ProductOverview
                {
                    ProductId = Util.GetValueOfInt(dr["M_Product_ID"]),
                    ProductName = Util.GetValueOfString(dr["Product_Name"]),
                    ProductCode = Util.GetValueOfString(dr["Product_Code"]),
                    SKU = Util.GetValueOfString(dr["SKU"]),
                    UPC = Util.GetValueOfString(dr["UPC"]),
                    ProductType = Util.GetValueOfString(dr["Product_Type"]),
                    CategoryName = Util.GetValueOfString(dr["Category_Name"]),
                    UomName = Util.GetValueOfString(dr["UOM_Name"]),
                    IsActive = Util.GetValueOfString(dr["IsActive"]) == "Y",
                    Discontinued = Util.GetValueOfString(dr["Discontinued"]) == "Y",
                    DiscontinuedFrom = FormatDate(Util.GetValueOfDateTime(dr["DiscontinuedBy"])),
                    ImageUrl = GetProductImageUrl(ctx, adImageId, imageExtension, storedImageUrl)
                };
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>
        /// Resolves a product's AD_Image record to something the browser can render:
        /// 1. AD_Image.ImageURL that is already absolute (http/https/data:) - returned as-is.
        /// 2. A file on this web server under GlobalVariable.ImagePath - same convention as
        ///    VASAttachUserToBP / VAS_TimeSheetInvoice / VAS_074_CreateInvoiceLinePanelModel.
        ///    The original upload is preferred (hero-size), then the largest thumbnail.
        ///    Only URLs whose file actually exists are returned, so the client never gets a 404.
        /// 3. AD_Image.BinaryData (images stored in the database) - inlined as a base64 data URI.
        /// Returns null when nothing is available, so the client falls back to the icon.
        /// The client prepends VIS.Application.contextUrl to relative results.
        /// </summary>
        /// <param name="adImageId">M_Product.AD_Image_ID</param>
        /// <param name="imageExtension">AD_Image.ImageExtension (e.g. ".png")</param>
        /// <param name="storedImageUrl">AD_Image.ImageURL (e.g. "Images/1004162.png")</param>
        private string GetProductImageUrl(Ctx ctx, int adImageId, string imageExtension, string storedImageUrl)
        {
            if (ctx == null || adImageId <= 0) { return null; }

            // 1. Image hosted elsewhere (external server / CDN) or already inlined.
            if (!string.IsNullOrEmpty(storedImageUrl)
                && (storedImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || storedImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || storedImageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
            {
                return storedImageUrl;
            }

            // 2. File saved on this web server by the image upload flow.
            string fileName = GetImageFileName(adImageId, imageExtension, storedImageUrl);
            if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(GlobalVariable.ImagePath))
            {
                string[] subFolders = { "", "Thumb500x375", "Thumb320x240", "Thumb320x185", "Thumb140x120", "Thumb46x46" };
                foreach (string subFolder in subFolders)
                {
                    string folder = subFolder.Length == 0
                        ? GlobalVariable.ImagePath
                        : System.IO.Path.Combine(GlobalVariable.ImagePath, subFolder);
                    if (System.IO.File.Exists(System.IO.Path.Combine(folder, fileName)))
                    {
                        return subFolder.Length == 0
                            ? "Images/" + fileName
                            : "Images/" + subFolder + "/" + fileName;
                    }
                }
            }

            // 3. Image bytes stored in the database (AD_Image.BinaryData).
            MImage image = MImage.Get(ctx, adImageId);
            if (image != null)
            {
                byte[] data = image.GetBinaryData();
                if (data != null && data.Length > 0)
                {
                    return "data:" + GetImageMimeType(imageExtension, data) + ";base64," + Convert.ToBase64String(data);
                }
            }

            return null;
        }

        /// <summary>
        /// File name of the uploaded image: the name stored on AD_Image.ImageURL when set
        /// (stripped of any folders, so the value can never escape the Images directory),
        /// otherwise the upload convention &lt;AD_Image_ID&gt;&lt;extension&gt;.
        /// </summary>
        private string GetImageFileName(int adImageId, string imageExtension, string storedImageUrl)
        {
            if (!string.IsNullOrEmpty(storedImageUrl))
            {
                try
                {
                    string fileName = System.IO.Path.GetFileName(storedImageUrl.Trim());
                    if (!string.IsNullOrEmpty(fileName)) { return fileName; }
                }
                catch (ArgumentException)
                {
                    // Invalid path characters in the stored URL - fall through to the convention.
                }
            }
            return !string.IsNullOrEmpty(imageExtension) ? adImageId + imageExtension : null;
        }

        private string GetImageMimeType(string imageExtension, byte[] data)
        {
            // The stored extension is unreliable for BLOB images, so sniff the magic bytes first.
            if (data != null && data.Length > 11)
            {
                if (data[0] == 0xFF && data[1] == 0xD8) { return "image/jpeg"; }
                if (data[0] == 0x89 && data[1] == 0x50) { return "image/png"; }
                if (data[0] == 0x47 && data[1] == 0x49) { return "image/gif"; }
                if (data[0] == 0x42 && data[1] == 0x4D) { return "image/bmp"; }
                if (data[0] == 0x52 && data[1] == 0x49 && data[8] == 0x57 && data[9] == 0x45) { return "image/webp"; }
            }

            switch ((imageExtension ?? "").Trim().ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".webp": return "image/webp";
                default: return "image/png";
            }
        }

        /// <summary>
        /// Weighted-average unit cost per attribute set instance, derived from CostRows:
        /// ROUND(SUM(price*qty)/SUM(qty),10) following the query supplied by VA (review #7).
        /// </summary>
        private const string AttributeCostSql = @"
                SELECT CostRows.M_Product_ID,
                       CostRows.M_AttributeSetInstance_ID,
                       ROUND(CostRows.Cost_Amount / NULLIF(CostRows.Cost_Qty,0), 10) AS Unit_Cost
                FROM CostRows";

        /// <summary>
        /// Weighted-average unit cost per product across all attribute set instances,
        /// used as the fallback when a storage row's attribute has no cost of its own.
        /// </summary>
        private const string ProductCostSql = @"
                SELECT CostRows.M_Product_ID,
                       ROUND(SUM(CostRows.Cost_Amount) / NULLIF(SUM(CostRows.Cost_Qty),0), 10) AS Unit_Cost
                FROM CostRows
                GROUP BY CostRows.M_Product_ID";

        /// <summary>
        /// Cost rows per product + attribute set instance following the costing query
        /// supplied by VA (review #7): M_Cost joined to the account schema's cost type,
        /// with the cost element resolved from the category's costing method (or the
        /// schema's when the category has none; 'C' uses the configured element directly),
        /// aggregated as SUM(CurrentCostPrice*CurrentQty) / SUM(CurrentQty) upstream.
        /// </summary>
        /// <param name="productFilter">SQL condition limiting Cost.M_Product_ID</param>
        private string BuildCostRowsSql(Ctx ctx, string productFilter)
        {
            string sql = @"
                SELECT Cost.M_Product_ID,
                       COALESCE(Cost.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       SUM(COALESCE(Cost.CurrentCostPrice,0) * COALESCE(Cost.CurrentQty,0)) AS Cost_Amount,
                       SUM(COALESCE(Cost.CurrentQty,0)) AS Cost_Qty
                FROM M_Cost Cost
                INNER JOIN M_Product CostProduct ON (CostProduct.M_Product_ID=Cost.M_Product_ID AND CostProduct.IsActive=N'Y')
                LEFT OUTER JOIN M_Product_Category CostCategory ON (CostCategory.M_Product_Category_ID=CostProduct.M_Product_Category_ID AND CostCategory.IsActive=N'Y')
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=Cost.C_AcctSchema_ID AND AcctSchema.IsActive=N'Y')
                INNER JOIN M_CostType CostType ON (CostType.M_CostType_ID=AcctSchema.M_CostType_ID AND CostType.M_CostType_ID=Cost.M_CostType_ID AND CostType.IsActive=N'Y')
                INNER JOIN M_CostElement CostElement ON (CostElement.M_CostElement_ID=Cost.M_CostElement_ID AND CostElement.IsActive=N'Y')
                WHERE Cost.IsActive=N'Y'
                  AND Cost.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND Cost.AD_Client_ID=@Cost_Client_ID
                  AND COALESCE(NULLIF(CostCategory.CostingMethod,''),AcctSchema.CostingMethod)=CostElement.CostingMethod
                  AND (CASE
                           WHEN NULLIF(CostCategory.CostingMethod,'') IS NOT NULL AND CostCategory.CostingMethod='C' THEN CostCategory.M_CostElement_ID
                           WHEN NULLIF(CostCategory.CostingMethod,'') IS NOT NULL THEN (SELECT MIN(CategoryElement.M_CostElement_ID) FROM M_CostElement CategoryElement WHERE CategoryElement.CostingMethod=CostCategory.CostingMethod AND CategoryElement.AD_Client_ID=@Element_Client_ID1 AND CategoryElement.IsActive=N'Y')
                           WHEN AcctSchema.CostingMethod='C' THEN AcctSchema.M_CostElement_ID
                           ELSE (SELECT MIN(SchemaElement.M_CostElement_ID) FROM M_CostElement SchemaElement WHERE SchemaElement.CostingMethod=AcctSchema.CostingMethod AND SchemaElement.AD_Client_ID=@Element_Client_ID2 AND SchemaElement.IsActive=N'Y')
                       END)=CostElement.M_CostElement_ID";

            sql = AddAccessSql(ctx, sql, "Cost");
            sql += @"
                  AND " + productFilter + @"
                GROUP BY Cost.M_Product_ID,
                         COALESCE(Cost.M_AttributeSetInstance_ID,0)";
            return sql;
        }

        private List<ProductStockRow> GetStockRows(Ctx ctx, int productId, SchemaCurrency currency)
        {
            string costRowsSql = BuildCostRowsSql(ctx, "Cost.M_Product_ID=@Cost_Product_ID");

            // Stock is broken out per attribute set instance (review #3) so material
            // held with an attribute shows on its own row with the attribute text.
            string storageRowsSql = @"
                SELECT Storage.M_Product_ID,
                       Warehouse.Name AS Warehouse_Name,
                       Locator.Value AS Locator_Value,
                       COALESCE(Storage.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       COALESCE(AttributeInstance.Description,N'') AS Attribute_Description,
                       SUM(COALESCE(Storage.QtyOnHand,0)) AS Qty_On_Hand
                FROM M_Storage Storage
                INNER JOIN M_Locator Locator ON (Locator.M_Locator_ID=Storage.M_Locator_ID AND Locator.IsActive=N'Y')
                INNER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=Locator.M_Warehouse_ID AND Warehouse.IsActive=N'Y')
                LEFT OUTER JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=Storage.M_AttributeSetInstance_ID)
                WHERE Storage.IsActive=N'Y'
                  AND Storage.M_Product_ID=@Storage_Product_ID
                  AND Storage.AD_Client_ID=@Storage_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@Storage_Org_ID,0),Storage.AD_Org_ID))";

            storageRowsSql = AddAccessSql(ctx, storageRowsSql, "Storage");
            storageRowsSql += @"
                GROUP BY Storage.M_Product_ID,
                         Warehouse.Name,
                         Locator.Value,
                         COALESCE(Storage.M_AttributeSetInstance_ID,0),
                         COALESCE(AttributeInstance.Description,N'')";

            string sql = @"
                WITH CostRows AS (
                    " + costRowsSql + @"
                ),
                AttributeCost AS (
                    " + AttributeCostSql + @"
                ),
                ProductCost AS (
                    " + ProductCostSql + @"
                ),
                StorageRows AS (
                    " + storageRowsSql + @"
                )
                SELECT StorageRows.Warehouse_Name,
                       StorageRows.Locator_Value,
                       StorageRows.Attribute_Description,
                       StorageRows.Qty_On_Hand,
                       StorageRows.Qty_On_Hand * COALESCE(AttributeCost.Unit_Cost,ProductCost.Unit_Cost,0) AS Stock_Value
                FROM StorageRows
                LEFT OUTER JOIN AttributeCost ON (AttributeCost.M_Product_ID=StorageRows.M_Product_ID AND AttributeCost.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN ProductCost ON (ProductCost.M_Product_ID=StorageRows.M_Product_ID)
                ORDER BY StorageRows.Warehouse_Name,
                         StorageRows.Locator_Value,
                         StorageRows.Attribute_Description";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_AcctSchema_ID", currency.AcctSchemaId),
                new SqlParameter("@Cost_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Element_Client_ID1", ctx.GetAD_Client_ID()),
                new SqlParameter("@Element_Client_ID2", ctx.GetAD_Client_ID()),
                new SqlParameter("@Cost_Product_ID", productId),
                new SqlParameter("@Storage_Product_ID", productId),
                new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Storage_Org_ID", ctx.GetAD_Org_ID())
            };

            List<ProductStockRow> rows = new List<ProductStockRow>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    rows.Add(new ProductStockRow
                    {
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        LocatorValue = Util.GetValueOfString(dr["Locator_Value"]),
                        Attribute = Util.GetValueOfString(dr["Attribute_Description"]),
                        Quantity = Util.GetValueOfDecimal(dr["Qty_On_Hand"]),
                        StockValue = Util.GetValueOfDecimal(dr["Stock_Value"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return rows;
        }

        private decimal GetReorderPoint(Ctx ctx, int productId)
        {
            string sql = @"
                SELECT Replenish.M_Warehouse_ID,
                       CASE
                           WHEN Replenish.ReplenishType=N'1' THEN Replenish.Level_Min
                           WHEN Replenish.ReplenishType=N'2' THEN Replenish.Level_Max
                           ELSE NULL
                       END AS Reorder_Point
                FROM M_Replenish Replenish
                WHERE Replenish.M_Product_ID=@M_Product_ID
                  AND Replenish.IsActive=N'Y'
                  AND Replenish.ReplenishType IN (N'1',N'2')
                  AND Replenish.AD_Client_ID=@AD_Client_ID
                  AND Replenish.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),Replenish.AD_Org_ID))";

            sql = AddAccessSql(ctx, sql, "Replenish");

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID())
            };

            decimal reorderPoint = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    decimal current = Util.GetValueOfDecimal(dr["Reorder_Point"]);
                    if (current > reorderPoint) { reorderPoint = current; }
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return reorderPoint;
        }

        private string GetPreferredSupplier(Ctx ctx, int productId)
        {
            string sql = @"
                SELECT BPartner.Name AS Supplier_Name
                FROM M_Product_PO ProductPO
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=ProductPO.C_BPartner_ID AND BPartner.IsActive=N'Y')
                WHERE ProductPO.M_Product_ID=@M_Product_ID
                  AND ProductPO.IsActive=N'Y'
                  AND ProductPO.AD_Client_ID=@AD_Client_ID
                  AND ProductPO.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),ProductPO.AD_Org_ID))";

            sql = AddAccessSql(ctx, sql, "ProductPO");
            sql += @"
                ORDER BY BPartner.Name
                OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID())
            };

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                return dr != null && dr.Read()
                    ? Util.GetValueOfString(dr["Supplier_Name"])
                    : "";
            }
            finally
            {
                CloseReader(dr);
            }
        }

        private List<ProductOrderRow> GetOrders(Ctx ctx, int productId, bool isSales, int pageSize)
        {
            string sql = @"
                SELECT OrderHeader.DocumentNo,
                       OrderHeader.DateOrdered,
                       COALESCE(OrderLine.DatePromised,OrderHeader.DatePromised) AS DatePromised,
                       OrderLine.QtyOrdered,
                       OrderLine.QtyDelivered,
                       OrderLine.LineNetAmt,
                       OrderHeader.DocStatus,
                       COALESCE(AttributeInstance.Description,N'') AS Attribute_Description,
                       CASE
                           WHEN OrderCurrency.CurSymbol IS NOT NULL THEN OrderCurrency.CurSymbol
                           ELSE OrderCurrency.ISO_Code
                       END AS Currency_Symbol,
                       OrderCurrency.ISO_Code AS Currency_ISO,
                       OrderCurrency.StdPrecision
                FROM C_OrderLine OrderLine
                INNER JOIN C_Order OrderHeader ON (OrderHeader.C_Order_ID=OrderLine.C_Order_ID AND OrderHeader.IsActive=N'Y')
                INNER JOIN C_Currency OrderCurrency ON (OrderCurrency.C_Currency_ID=OrderHeader.C_Currency_ID AND OrderCurrency.IsActive=N'Y')
                LEFT OUTER JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=OrderLine.M_AttributeSetInstance_ID)
                WHERE OrderLine.M_Product_ID=@M_Product_ID
                  AND OrderHeader.IsSOTrx=@IsSOTrx
                  AND OrderLine.IsActive=N'Y'
                  AND OrderLine.AD_Client_ID=@AD_Client_ID
                  AND OrderLine.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),OrderLine.AD_Org_ID))";

            sql = AddAccessSql(ctx, sql, "OrderLine");
            sql += @"
                ORDER BY OrderHeader.DateOrdered DESC,
                         OrderLine.C_OrderLine_ID DESC
                OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@IsSOTrx", SqlDbType.VarChar) { Value = isSales ? "Y" : "N" },
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@PageSize", pageSize)
            };

            List<ProductOrderRow> rows = new List<ProductOrderRow>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    rows.Add(new ProductOrderRow
                    {
                        DocumentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        DateOrdered = FormatDate(Util.GetValueOfDateTime(dr["DateOrdered"])),
                        DatePromised = FormatDate(Util.GetValueOfDateTime(dr["DatePromised"])),
                        QuantityOrdered = Util.GetValueOfDecimal(dr["QtyOrdered"]),
                        QuantityDelivered = Util.GetValueOfDecimal(dr["QtyDelivered"]),
                        LineNetAmount = Util.GetValueOfDecimal(dr["LineNetAmt"]),
                        DocumentStatus = Util.GetValueOfString(dr["DocStatus"]),
                        Attribute = Util.GetValueOfString(dr["Attribute_Description"]),
                        CurrencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]),
                        CurrencyIso = Util.GetValueOfString(dr["Currency_ISO"]),
                        StdPrecision = Util.GetValueOfInt(dr["StdPrecision"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return rows;
        }

        private List<ProductMovementRow> GetMovements(Ctx ctx, int productId, int pageSize)
        {
            string sql = @"
                SELECT Movement.MovementDate,
                       Movement.MovementType,
                       Movement.MovementQty,
                       Warehouse.Name AS Warehouse_Name,
                       Locator.Value AS Locator_Value,
                       COALESCE(AttributeInstance.Description,N'') AS Attribute_Description
                FROM M_Transaction Movement
                INNER JOIN M_Locator Locator ON (Locator.M_Locator_ID=Movement.M_Locator_ID AND Locator.IsActive=N'Y')
                INNER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=Locator.M_Warehouse_ID AND Warehouse.IsActive=N'Y')
                LEFT OUTER JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=Movement.M_AttributeSetInstance_ID)
                WHERE Movement.M_Product_ID=@M_Product_ID
                  AND Movement.IsActive=N'Y'
                  AND Movement.AD_Client_ID=@AD_Client_ID
                  AND Movement.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),Movement.AD_Org_ID))";

            // Review #36: filter reversed movements only where the column exists.
            if (TransactionHasIsReversed())
            {
                sql += @"
                  AND COALESCE(Movement.IsReversed,'N')='N'";
            }

            sql = AddAccessSql(ctx, sql, "Movement");
            sql += @"
                ORDER BY Movement.MovementDate DESC,
                         Movement.Created DESC
                OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@PageSize", pageSize)
            };

            List<ProductMovementRow> rows = new List<ProductMovementRow>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    rows.Add(new ProductMovementRow
                    {
                        MovementDate = FormatDateTime(Util.GetValueOfDateTime(dr["MovementDate"])),
                        MovementType = Util.GetValueOfString(dr["MovementType"]),
                        MovementQuantity = Util.GetValueOfDecimal(dr["MovementQty"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        LocatorValue = Util.GetValueOfString(dr["Locator_Value"]),
                        Attribute = Util.GetValueOfString(dr["Attribute_Description"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return rows;
        }

        private List<ProductRequisitionRow> GetRequisitions(Ctx ctx, int productId, int pageSize)
        {
            string sql = @"
                SELECT Requisition.DocumentNo,
                       Requisition.DateDoc,
                       Requisition.DateRequired,
                       RequisitionLine.Qty,
                       RequisitionLine.QtyOrdered,
                       Requisition.DocStatus,
                       COALESCE(AttributeInstance.Description,N'') AS Attribute_Description
                FROM M_RequisitionLine RequisitionLine
                INNER JOIN M_Requisition Requisition ON (Requisition.M_Requisition_ID=RequisitionLine.M_Requisition_ID AND Requisition.IsActive=N'Y')
                LEFT OUTER JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=RequisitionLine.M_AttributeSetInstance_ID)
                WHERE RequisitionLine.M_Product_ID=@M_Product_ID
                  AND RequisitionLine.IsActive=N'Y'
                  AND RequisitionLine.AD_Client_ID=@AD_Client_ID
                  AND RequisitionLine.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),RequisitionLine.AD_Org_ID))";

            sql = AddAccessSql(ctx, sql, "RequisitionLine");
            sql += @"
                ORDER BY Requisition.DateDoc DESC,
                         RequisitionLine.M_RequisitionLine_ID DESC
                OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@PageSize", pageSize)
            };

            List<ProductRequisitionRow> rows = new List<ProductRequisitionRow>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    rows.Add(new ProductRequisitionRow
                    {
                        DocumentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        DocumentDate = FormatDate(Util.GetValueOfDateTime(dr["DateDoc"])),
                        RequiredDate = FormatDate(Util.GetValueOfDateTime(dr["DateRequired"])),
                        Quantity = Util.GetValueOfDecimal(dr["Qty"]),
                        QuantityOrdered = Util.GetValueOfDecimal(dr["QtyOrdered"]),
                        DocumentStatus = Util.GetValueOfString(dr["DocStatus"]),
                        Attribute = Util.GetValueOfString(dr["Attribute_Description"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return rows;
        }

        private SchemaCurrency GetSchemaCurrency(Ctx ctx)
        {
            SchemaCurrency currency = new SchemaCurrency();
            if (ctx == null) { return currency; }

            string sql = @"
                SELECT ClientInfo.C_AcctSchema1_ID AS C_AcctSchema_ID,
                       AcctSchema.C_Currency_ID,
                       AcctSchema.M_CostType_ID,
                       AcctSchema.M_CostElement_ID,
                       COALESCE(NULLIF(AcctSchema.CostingMethod,''),'S') AS Costing_Method,
                       Currency.StdPrecision,
                       CASE
                           WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                           ELSE Currency.ISO_Code
                       END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive=N'Y')
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID AND Currency.IsActive=N'Y')
                WHERE ClientInfo.IsActive=N'Y'
                  AND ClientInfo.AD_Client_ID=@AD_Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "ClientInfo",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }
                );

                if (reader == null || !reader.Read()) { return currency; }

                currency.AcctSchemaId = Util.GetValueOfInt(reader["C_AcctSchema_ID"]);
                currency.CurrencyId = Util.GetValueOfInt(reader["C_Currency_ID"]);
                currency.CostTypeId = Util.GetValueOfInt(reader["M_CostType_ID"]);
                currency.CostElementId = Util.GetValueOfInt(reader["M_CostElement_ID"]);
                currency.CostingMethod = Util.GetValueOfString(reader["Costing_Method"]);
                currency.Symbol = Util.GetValueOfString(reader["Currency_Symbol"]);
                currency.IsoCode = Util.GetValueOfString(reader["Currency_ISO"]);
                currency.StdPrecision = Util.GetValueOfInt(reader["StdPrecision"]);
                return currency;
            }
            finally
            {
                if (reader != null) { reader.Close(); reader.Dispose(); }
            }
        }

        private string AddAccessSql(Ctx ctx, string sql, string tableAlias)
        {
            return MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                tableAlias,
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );
        }

        private string FormatDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "";
        }

        private string FormatDateTime(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "";
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class SchemaCurrency
        {
            public int AcctSchemaId { get; set; }
            public int CurrencyId { get; set; }
            public int CostTypeId { get; set; }
            public int CostElementId { get; set; }
            public string CostingMethod { get; set; }
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int StdPrecision { get; set; }
        }

        private class ProductSearchResult
        {
            public List<ProductSearchRow> Rows { get; set; }
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int StdPrecision { get; set; }
        }

        private class ProductSearchRow
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ProductCode { get; set; }
            public string SKU { get; set; }
            public string UPC { get; set; }
            public string ProductType { get; set; }
            public string CategoryName { get; set; }
            public decimal OnHandQty { get; set; }
            public decimal StockValue { get; set; }
        }

        private class ProductDetail
        {
            public ProductOverview Overview { get; set; }
            public decimal OnHandQty { get; set; }
            public decimal StockValue { get; set; }
            public decimal ReorderPoint { get; set; }
            public string Status { get; set; }
            public string PreferredSupplier { get; set; }
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int StdPrecision { get; set; }
            public List<ProductStockRow> Stock { get; set; }
            public List<ProductOrderRow> PurchaseOrders { get; set; }
            public List<ProductOrderRow> SalesOrders { get; set; }
            public List<ProductMovementRow> Movements { get; set; }
            public List<ProductRequisitionRow> Requisitions { get; set; }
        }

        private class ProductOverview
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ProductCode { get; set; }
            public string SKU { get; set; }
            public string UPC { get; set; }
            public string ProductType { get; set; }
            public string CategoryName { get; set; }
            public string UomName { get; set; }
            public bool IsActive { get; set; }
            public bool Discontinued { get; set; }
            public string DiscontinuedFrom { get; set; }
            public string ImageUrl { get; set; }
        }

        private class ProductStockRow
        {
            public string WarehouseName { get; set; }
            public string LocatorValue { get; set; }
            public string Attribute { get; set; }
            public decimal Quantity { get; set; }
            public decimal StockValue { get; set; }
        }

        private class ProductOrderRow
        {
            public string DocumentNo { get; set; }
            public string DateOrdered { get; set; }
            public string DatePromised { get; set; }
            public string Attribute { get; set; }
            public decimal QuantityOrdered { get; set; }
            public decimal QuantityDelivered { get; set; }
            public decimal LineNetAmount { get; set; }
            public string DocumentStatus { get; set; }
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int StdPrecision { get; set; }
        }

        private class ProductMovementRow
        {
            public string MovementDate { get; set; }
            public string MovementType { get; set; }
            public decimal MovementQuantity { get; set; }
            public string WarehouseName { get; set; }
            public string LocatorValue { get; set; }
            public string Attribute { get; set; }
        }

        private class ProductRequisitionRow
        {
            public string DocumentNo { get; set; }
            public string DocumentDate { get; set; }
            public string RequiredDate { get; set; }
            public string Attribute { get; set; }
            public decimal Quantity { get; set; }
            public decimal QuantityOrdered { get; set; }
            public string DocumentStatus { get; set; }
        }
    }
}
