using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using VAdvantage.Utility;
using VAdvantage.Model;
using System.Data;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using System.Text;
using System.Data.SqlClient;

namespace VIS.Models
{
    public class RelatedProductModel
    {
        /// <summary>
        /// Getting Product Data
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="M_Product_ID">Product</param>
        /// <param name="M_AttributeSetInstance_ID">Attribute</param>
        /// <param name="M_PriceList_ID">Price List</param>
        /// <param name="Table_ID">Table ID</param>
        /// <param name="Record_ID">Record ID</param>
        /// <returns>Product Details</returns>
        public RelatedProductData GetProductData(Ctx ctx, int M_Product_ID, int M_AttributeSetInstance_ID, int C_UOM_ID, int M_PriceList_ID, int Table_ID, int Record_ID)
        {
            RelatedProductData res = null;
            DateTime? validFrom = null;

            if (MOrder.Table_ID.Equals(Table_ID))
            {

                validFrom = Util.GetValueOfDateTime(DB.ExecuteScalar("SELECT DateOrdered FROM C_Order WHERE C_Order_ID = " + Record_ID));
            }

            if (validFrom == null)
            {
                validFrom = DateTime.Now.Date;
            }

            int M_PriceList_Version_ID = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT M_PriceList_Version_ID FROM M_PriceList_Version WHERE IsActive = 'Y' 
                                AND M_PriceList_ID = " + M_PriceList_ID + @" AND ValidFrom <= " + DB.TO_DATE(validFrom, true) + " ORDER BY ValidFrom DESC"));

            string sql = "SELECT P.M_Product_ID, " + (C_UOM_ID > 0 ? C_UOM_ID.ToString() : "P.C_UOM_ID") + @" AS C_UOM_ID, P.Value, P.Name, 1 AS QtyEntered,
                    BOMPRICELISTUOM(P.M_PRODUCT_ID, PR.M_PRICELIST_VERSION_ID, PR.M_ATTRIBUTESETINSTANCE_ID, PR.C_UOM_ID) AS PriceList,
                    BOMPRICESTDUOM(P.M_PRODUCT_ID, PR.M_PRICELIST_VERSION_ID, PR.M_ATTRIBUTESETINSTANCE_ID, PR.C_UOM_ID) AS UnitPrice,
                    BOMPRICELIMITUOM(P.M_PRODUCT_ID, PR.M_PRICELIST_VERSION_ID, PR.M_ATTRIBUTESETINSTANCE_ID, PR.C_UOM_ID) AS LimitPrice
                    FROM M_PRODUCT P LEFT JOIN M_PRODUCTPRICE PR ON ( P.M_PRODUCT_ID = PR.M_PRODUCT_ID AND PR.ISACTIVE = 'Y' 
                    AND NVL(PR.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID + " AND PR.M_PRICELIST_VERSION_ID = " + M_PriceList_Version_ID
                    + " AND PR.C_UOM_ID = " + (C_UOM_ID > 0 ? C_UOM_ID.ToString() : "P.C_UOM_ID") + @") WHERE P.M_Product_ID = " + M_Product_ID;
            DataSet _ds = DB.ExecuteDataset(sql, null, null);
            if (_ds != null && _ds.Tables[0].Rows.Count > 0)
            {
                res = new RelatedProductData();
                res.Product_ID = Util.GetValueOfInt(_ds.Tables[0].Rows[0]["M_Product_ID"]);
                res.Product = Util.GetValueOfString(_ds.Tables[0].Rows[0]["Name"]);
                res.AttributeSetInstance_ID = M_AttributeSetInstance_ID;
                res.Value = Util.GetValueOfString(_ds.Tables[0].Rows[0]["Value"]);
                //res.UOM = Util.GetValueOfString(_ds.Tables[0].Rows[0]["UName"]);
                res.UOM_ID = Util.GetValueOfInt(_ds.Tables[0].Rows[0]["C_UOM_ID"]);
                res.QtyEntered = Util.GetValueOfDecimal(_ds.Tables[0].Rows[0]["QtyEntered"]);
                res.PriceStd = Util.GetValueOfDecimal(_ds.Tables[0].Rows[0]["UnitPrice"]);
                res.PriceList = Util.GetValueOfDecimal(_ds.Tables[0].Rows[0]["PriceList"]);
            }
            return res;
        }

        /// <summary>
        /// Getting Related Product Data
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="M_Product_ID">Product</param>
        /// <param name="M_AttributeSetInstance_ID">Attribute</param>
        /// <param name="M_PriceList_ID">Price List</param>
        /// <param name="Table_ID">Table ID</param>
        /// <param name="Record_ID">Record ID</param>
        /// <returns></returns>
        public List<RelatedProductData> GetRelatedProduct(Ctx ctx, int M_Product_ID, int M_AttributeSetInstance_ID, int C_UOM_ID, int M_PriceList_ID, string Relatedtype, int Table_ID, int Record_ID)
        {
            List<RelatedProductData> retData = null;
            RelatedProductData res;
            DateTime? validFrom = null;

            if (MOrder.Table_ID.Equals(Table_ID))
            {
                validFrom = Util.GetValueOfDateTime(DB.ExecuteScalar("SELECT DateOrdered FROM C_Order WHERE C_Order_ID = " + Record_ID));
            }

            if (validFrom == null)
            {
                validFrom = DateTime.Now.Date;
            }

            int M_PriceList_Version_ID = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT M_PriceList_Version_ID FROM M_PriceList_Version WHERE IsActive = 'Y' 
                                AND M_PriceList_ID = " + M_PriceList_ID + @" AND ValidFrom <= " + DB.TO_DATE(validFrom, true) + " ORDER BY ValidFrom DESC"));

            // Only the parameters actually used: a null entry makes ExecuteDataset fail when no type is selected
            List<SqlParameter> param = new List<SqlParameter>();
            // P.Name is the related product itself; R.Name is the name of the relation (shown as a second line)
            string sql = @"SELECT P.M_Product_ID, " + (C_UOM_ID > 0 ? C_UOM_ID.ToString() : "P.C_UOM_ID") + @" AS C_UOM_ID, P.Value, P.Name AS ProductName, R.Name AS RelationName, 1 AS QtyEntered,
                    R.RelatedProductType, (SELECT Name FROM AD_Ref_List WHERE AD_Reference_ID=(SELECT AD_Reference_ID FROM AD_Reference WHERE Name='M_RelatedProduct Type')
                    AND Value = R.RelatedProductType) AS RelatedType,
                    BOMPRICELISTUOM(P.M_PRODUCT_ID, PR.M_PRICELIST_VERSION_ID, PR.M_ATTRIBUTESETINSTANCE_ID, PR.C_UOM_ID) AS PriceList,
                    BOMPRICESTDUOM(P.M_PRODUCT_ID, PR.M_PRICELIST_VERSION_ID, PR.M_ATTRIBUTESETINSTANCE_ID, PR.C_UOM_ID) AS UnitPrice,
                    BOMPRICELIMITUOM(P.M_PRODUCT_ID, PR.M_PRICELIST_VERSION_ID, PR.M_ATTRIBUTESETINSTANCE_ID, PR.C_UOM_ID) AS LimitPrice
                    FROM M_RelatedProduct R INNER JOIN M_PRODUCT P ON (p.M_Product_ID = R.RelatedProduct_ID) LEFT JOIN M_PRODUCTPRICE PR ON ( P.M_PRODUCT_ID = PR.M_PRODUCT_ID AND PR.ISACTIVE = 'Y' 
                    AND NVL(PR.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID + " AND PR.M_PRICELIST_VERSION_ID = " + M_PriceList_Version_ID
                    + " AND PR.C_UOM_ID = " + (C_UOM_ID > 0 ? C_UOM_ID.ToString() : "P.C_UOM_ID") + @") WHERE R.IsActive = 'Y' AND R.M_Product_ID = " + M_Product_ID;
            if (!string.IsNullOrEmpty(Relatedtype))
            {
                param.Add(new SqlParameter("@type", Relatedtype));
                sql += " AND R.RelatedProductType=@type";
            }
            DataSet _ds = DB.ExecuteDataset(sql, param.Count > 0 ? param.ToArray() : null, null);
            if (_ds != null && _ds.Tables[0].Rows.Count > 0)
            {
                retData = new List<RelatedProductData>();
                for (int i = 0; i < _ds.Tables[0].Rows.Count; i++)
                {
                    res = new RelatedProductData();
                    res.Product_ID = Util.GetValueOfInt(_ds.Tables[0].Rows[i]["M_Product_ID"]);
                    res.Product = Util.GetValueOfString(_ds.Tables[0].Rows[i]["ProductName"]);
                    res.RelationName = Util.GetValueOfString(_ds.Tables[0].Rows[i]["RelationName"]);
                    res.RelatedProductType = Util.GetValueOfString(_ds.Tables[0].Rows[i]["RelatedProductType"]);
                    res.RelatedType = Util.GetValueOfString(_ds.Tables[0].Rows[i]["RelatedType"]);
                    res.AttributeSetInstance_ID = M_AttributeSetInstance_ID;
                    res.Value = Util.GetValueOfString(_ds.Tables[0].Rows[i]["Value"]);
                    //res.UOM = Util.GetValueOfString(_ds.Tables[0].Rows[i]["UName"]);
                    res.UOM_ID = Util.GetValueOfInt(_ds.Tables[0].Rows[i]["C_UOM_ID"]);
                    res.QtyEntered = Util.GetValueOfDecimal(_ds.Tables[0].Rows[i]["QtyEntered"]);
                    res.PriceStd = Util.GetValueOfDecimal(_ds.Tables[0].Rows[i]["UnitPrice"]);
                    res.PriceList = Util.GetValueOfDecimal(_ds.Tables[0].Rows[i]["PriceList"]);
                    retData.Add(res);
                }
            }
            return retData;
        }

        /// <summary>
        /// Header details of the order or opportunity the pop-up adds lines to: document number,
        /// business partner, price list and currency (ISO code and precision for amount display)
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="Table_ID">C_Order or C_Project</param>
        /// <param name="Record_ID">Record ID</param>
        /// <returns>Header details, or null when the record is not found</returns>
        public RelatedHeaderInfo GetHeaderInfo(Ctx ctx, int Table_ID, int Record_ID)
        {
            string sql;
            string alias;
            if (MOrder.Table_ID.Equals(Table_ID))
            {
                alias = "o";
                sql = @"SELECT o.DocumentNo, bp.Name AS BPName, o.M_PriceList_ID, c.ISO_Code, c.StdPrecision,
                        COALESCE(dtt.Name, dt.Name) AS DocTypeName
                        FROM C_Order o
                        INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=o.C_BPartner_ID)
                        INNER JOIN C_Currency c ON (c.C_Currency_ID=o.C_Currency_ID)
                        LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID=o.C_DocTypeTarget_ID)
                        LEFT OUTER JOIN C_DocType_Trl dtt ON (dtt.C_DocType_ID=dt.C_DocType_ID AND dtt.AD_Language=@lang)
                        WHERE o.IsActive='Y' AND o.C_Order_ID=@recordId";
            }
            else if (MProject.Table_ID.Equals(Table_ID))
            {
                alias = "p";
                sql = @"SELECT p.Value AS DocumentNo, bp.Name AS BPName,
                        COALESCE(plv.M_PriceList_ID, p.M_PriceList_ID) AS M_PriceList_ID, c.ISO_Code, c.StdPrecision,
                        NULL AS DocTypeName
                        FROM C_Project p
                        LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=p.C_BPartner_ID)
                        LEFT OUTER JOIN M_PriceList_Version plv ON (plv.M_PriceList_Version_ID=p.M_PriceList_Version_ID)
                        LEFT OUTER JOIN C_Currency c ON (c.C_Currency_ID=p.C_Currency_ID)
                        WHERE p.IsActive='Y' AND p.C_Project_ID=@recordId";
            }
            else
            {
                return null;
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            List<SqlParameter> param = new List<SqlParameter>();
            if (alias == "o")
            {
                param.Add(new SqlParameter("@lang", ctx.GetAD_Language()));
            }
            param.Add(new SqlParameter("@recordId", Record_ID));
            DataSet ds = DB.ExecuteDataset(sql, param.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return null;
            }

            DataRow dr = ds.Tables[0].Rows[0];
            RelatedHeaderInfo info = new RelatedHeaderInfo();
            info.DocumentNo = Util.GetValueOfString(dr["DocumentNo"]);
            info.BPName = Util.GetValueOfString(dr["BPName"]);
            info.M_PriceList_ID = Util.GetValueOfInt(dr["M_PriceList_ID"]);
            info.ISO_Code = Util.GetValueOfString(dr["ISO_Code"]);
            info.StdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
            info.DocTypeName = Util.GetValueOfString(dr["DocTypeName"]);
            return info;
        }

        /// <summary>
        /// Creatd Related Product Lines
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="ReqLines">Related Product Data</param>
        /// <param name="Table_ID">Table ID</param>
        /// <param name="Record_ID">Record ID</param>
        /// <returns>Saved and total line count, summary message and the reason each failed line was not saved</returns>
        public RelatedLinesResult CreateRelatedLines(Ctx ctx, List<RelatedProductData> ReqLines, int Table_ID, int Record_ID)
        {
            RelatedLinesResult result = new RelatedLinesResult();

            if (ReqLines == null || ReqLines.Count == 0)
            {
                result.Message = Msg.GetMsg(ctx, "VIS_PlzSelLines");
                return result;
            }

            if (MOrder.Table_ID.Equals(Table_ID))
            {
                result = CreateSQLines(ctx, ReqLines, Record_ID);
            }

            else if (MProject.Table_ID.Equals(Table_ID))
            {
                result = CreateOppLines(ctx, ReqLines, Record_ID);
            }

            return result;
        }

        /// <summary>
        /// Create Sales Quotation Lines
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="ReqLines">Related Product data</param>
        /// <param name="Record_ID">Record ID</param>
        /// <returns>Saved and total line count, summary message and failed lines</returns>
        public RelatedLinesResult CreateSQLines(Ctx ctx, List<RelatedProductData> ReqLines, int Record_ID)
        {
            int TotalCount, SavedCount = 0;
            int LineNo;
            RelatedLinesResult result = new RelatedLinesResult();
            MOrderLine POLine;

            LineNo = Util.GetValueOfInt(DB.ExecuteScalar("SELECT MAX(Line) FROM C_OrderLine WHERE C_Order_ID=" + Record_ID));
            MOrder PO = new MOrder(ctx, Record_ID, null);
            TotalCount = ReqLines.Count;
            for (int i = 0; i < ReqLines.Count; i++)
            {
                LineNo += 10;
                POLine = new MOrderLine(PO);
                POLine.SetM_Product_ID(ReqLines[i].Product_ID);
                POLine.SetM_AttributeSetInstance_ID(ReqLines[i].AttributeSetInstance_ID);
                POLine.SetC_UOM_ID(ReqLines[i].UOM_ID);
                POLine.SetQtyEntered(ReqLines[i].QtyEntered);
                POLine.SetQtyOrdered(ReqLines[i].QtyEntered);
                POLine.SetDatePromised(PO.GetDatePromised());
                POLine.SetLine(LineNo);
                // Line dates follow the order: today's date fails when the order is dated earlier than today
                // (line DateOrdered later than DatePromised -> VIS_OrderDateGrtrThanPromisedDate)
                POLine.SetDateOrdered(PO.GetDateOrdered());
                POLine.SetPriceEntered(ReqLines[i].PriceStd);
                POLine.SetPriceActual(ReqLines[i].PriceStd);
                POLine.SetPriceList(ReqLines[i].PriceList);
                if (POLine.Save())
                {
                    SavedCount++;
                }
                else
                {
                    AddSaveError(ctx, result, ReqLines[i], i);
                }
            }
            SetResultMessage(ctx, result, SavedCount, TotalCount);
            return result;
        }

        /// <summary>
        /// Adds the line that was not saved, with the reason from the save error, to the result
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="result">Result being built</param>
        /// <param name="line">Line that was not saved</param>
        /// <param name="index">Position of the line in the request, so the pop-up keeps only the failed lines</param>
        private void AddSaveError(Ctx ctx, RelatedLinesResult result, RelatedProductData line, int index)
        {
            ValueNamePair pp = VLogger.RetrieveError();
            // Model classes may save only a message key as the error value (e.g. VIS_OrderDateGrtrThanPromisedDate)
            string err = pp == null ? "" : (!string.IsNullOrEmpty(pp.GetName()) ? pp.GetName() : Msg.GetMsg(ctx, pp.GetValue()));
            result.Errors.Add(new RelatedLineError { LineIndex = index, Product = line.Product, Error = err });
        }

        /// <summary>
        /// Sets the counts and the summary message of the result
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="result">Result being built</param>
        /// <param name="savedCount">Lines saved</param>
        /// <param name="totalCount">Lines requested</param>
        private void SetResultMessage(Ctx ctx, RelatedLinesResult result, int savedCount, int totalCount)
        {
            result.Saved = savedCount;
            result.Total = totalCount;
            if (totalCount == savedCount)
            {
                result.Message = Msg.GetMsg(ctx, "VIS_SuccessFullyInserted");
            }
            else
            {
                result.Message = savedCount + Msg.GetMsg(ctx, "VIS_NoOfLineSaved") + totalCount + ".";
            }
        }

        /// <summary>
        /// Create Opportunity Lines
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="ReqLines">Related Product data</param>
        /// <param name="Record_ID">Record ID</param>
        /// <returns>Saved and total line count, summary message and failed lines</returns>
        public RelatedLinesResult CreateOppLines(Ctx ctx, List<RelatedProductData> ReqLines, int Record_ID)
        {
            int TotalCount, SavedCount = 0;
            int LineNo;
            Decimal Discount;
            RelatedLinesResult result = new RelatedLinesResult();
            MProjectLine POLine;

            LineNo = Util.GetValueOfInt(DB.ExecuteScalar("SELECT MAX(Line) FROM C_ProjectLine WHERE C_Project_ID=" + Record_ID));
            MProject PO = new MProject(ctx, Record_ID, null);
            TotalCount = ReqLines.Count;
            for (int i = 0; i < ReqLines.Count; i++)
            {
                LineNo += 10;
                POLine = new MProjectLine(PO);
                POLine.SetM_Product_ID(ReqLines[i].Product_ID);
                if (POLine.Get_ColumnIndex("M_AttributeSetInstance_ID") >= 0)
                {
                    POLine.SetM_AttributeSetInstance_ID(ReqLines[i].AttributeSetInstance_ID);
                }
                if (POLine.Get_ColumnIndex("C_UOM_ID") >= 0)
                {
                    POLine.Set_Value("C_UOM_ID", ReqLines[i].UOM_ID);
                }
                POLine.SetPlannedQty(ReqLines[i].QtyEntered);
                POLine.SetLine(LineNo);

                if (Math.Sign(ReqLines[i].PriceList) == 0)
                    Discount = Env.ZERO;
                else
                {
                    Decimal multiplier = Decimal.Round(Decimal.Divide(Decimal.Multiply(ReqLines[i].PriceStd, Env.ONEHUNDRED)
                        , ReqLines[i].PriceList), 2, MidpointRounding.AwayFromZero);
                    Discount = Decimal.Subtract(Env.ONEHUNDRED, multiplier);
                }
                POLine.SetDiscount(Discount);
                POLine.SetPlannedPrice(ReqLines[i].PriceStd);
                POLine.SetPriceList(ReqLines[i].PriceList);
                if (POLine.Save())
                {
                    SavedCount++;
                }
                else
                {
                    AddSaveError(ctx, result, ReqLines[i], i);
                }
            }
            SetResultMessage(ctx, result, SavedCount, TotalCount);
            return result;
        }
    }

    /// <summary>
    /// Header details shown at the top of the Add Related Products pop-up
    /// </summary>
    public class RelatedHeaderInfo
    {
        public string DocumentNo { get; set; }
        public string BPName { get; set; }
        public int M_PriceList_ID { get; set; }
        public string ISO_Code { get; set; }
        public int StdPrecision { get; set; }
        /// <summary>Document type of the order (translated); empty for an opportunity</summary>
        public string DocTypeName { get; set; }
    }

    /// <summary>
    /// Result of saving related product lines
    /// </summary>
    public class RelatedLinesResult
    {
        public int Saved { get; set; }
        public int Total { get; set; }
        public string Message { get; set; }
        public List<RelatedLineError> Errors { get; set; } = new List<RelatedLineError>();
    }

    /// <summary>
    /// A line that was not saved and why
    /// </summary>
    public class RelatedLineError
    {
        public int LineIndex { get; set; }
        public string Product { get; set; }
        public string Error { get; set; }
    }

    public class RelatedType
    {
        public string Value { get; set; }
        public string Name { get; set; }
    }

    public class RelatedProductData
    {
        public int Product_ID { get; set; }
        public int AttributeSetInstance_ID { get; set; }
        public string Value { get; set; }
        public string Product { get; set; }
        public string RelatedType { get; set; }
        public string RelatedProductType { get; set; }
        /// <summary>Name of the relation (M_RelatedProduct.Name); Product holds the product name</summary>
        public string RelationName { get; set; }
        // public string UOM { get; set; }
        public int UOM_ID { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal PriceStd { get; set; }
        public decimal PriceList { get; set; }
    }
}