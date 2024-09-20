/********************************************************
 * Module Name    : VIS
 * Purpose        : Model class Inventory line form
 * Class Used     : 
 * Chronological Development
 * Megha Rana    20-sept-24
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using ViennaAdvantage.Model;
using VIS.Models;

namespace VAS.Models
{
    public class InventoryLinesModel
    {
        private static VLogger _log = VLogger.GetVLogger(typeof(MInventory).FullName);
        /// <summary>
        /// VIS0336-using this method for fetching the users for create inventory line form
        /// </summary>
        /// <param name="SearchKey"></param>
        /// <returns>users</returns>
        public List<KeyNamePair> GetUsers(Ctx ctx, string value)
        {
            List<KeyNamePair> user = null;
            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT Name,AD_User_ID FROM AD_User WHERE IsActive='Y' ");
            if (!string.IsNullOrEmpty(value))
            {
                sql.Append(" AND UPPER(Name) LIKE UPPER('%" + value + "%') ");
            }
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                user = new List<KeyNamePair>();
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    KeyNamePair obj = new KeyNamePair();
                    obj.Key = Util.GetValueOfInt(row["AD_User_ID"]);
                    obj.Name = Util.GetValueOfString(row["Name"]);
                    user.Add(obj);
                }
            }
            return user;
        }
        // VIS0336-using this method for fetching the cart names and detail for inventory form
        /// </summary>
        /// <param name="CartName"></param>
        /// <param name="UserId"></param>
        /// <param name="FromDate"></param>
        /// <param name="ToDate"></param>
        /// <param name="RefNo"></param>
        /// <returns>carts</returns>
        public List<Dictionary<string, object>> GetIventoryCartData(string CartName, string UserId, string FromDate, string ToDate, string RefNo)
        {
            List<Dictionary<string, object>> retDic = null;
            Dictionary<string, object> obj = null;
            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT c.VAICNT_ScanName,c.VAICNT_TransactionType,a.Name,c.VAICNT_InventoryCount_ID, (SELECT NAME FROM AD_Ref_List WHERE AD_Reference_ID=" +
                " (SELECT AD_Reference_ID FROM AD_Reference WHERE Name='VAICNT_TransactionType') AND ISActive='Y' AND Value=VAICNT_TransactionType) AS TransactionType, " +
                " (SELECT COUNT(VAICNT_InventoryCount_ID) FROM VAICNT_InventoryCountLine WHERE VAICNT_InventoryCount_ID=c.VAICNT_InventoryCount_ID) AS LineCount " +
                " FROM VAICNT_InventoryCount  c INNER JOIN AD_User a ON a.AD_User_ID=c.CreatedBy WHERE VAICNT_TransactionType IN ('OT','PI')");

            if (!string.IsNullOrEmpty(CartName))
            {
                CartName = CartName.ToUpper();
                sql.Append(" AND(UPPER(c.VAICNT_ScanName) LIKE'%" + CartName + "%') ");

            }
            if (!string.IsNullOrEmpty(RefNo))
            {
                RefNo = RefNo.ToUpper();
                sql.Append(" AND(UPPER(c.VAICNT_ReferenceNo) LIKE'%" + RefNo + "%') ");

            }
            if (!string.IsNullOrEmpty(UserId))
            {
                sql.Append(" AND a.AD_User_Id IN (" + UserId + ")");

            }
            //If from is not empty but date is empty
            if (!string.IsNullOrEmpty(FromDate) && string.IsNullOrEmpty(ToDate))
            {
                sql.Append(@" AND TRUNC(c.Created) >=" + (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(FromDate), true)));

            }
            //if to date is not empty and from date is empty
            if (string.IsNullOrEmpty(FromDate) && !string.IsNullOrEmpty(ToDate))
            {
                sql.Append(@" AND TRUNC(c.Created) <=" + (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(ToDate), true)));

            }
            //if from and to date not empty
            if (!string.IsNullOrEmpty(FromDate) && !string.IsNullOrEmpty(ToDate))
            {
                sql.Append(@" AND TRUNC(c.Created) BETWEEN " +
              (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(FromDate), true)));
                sql.Append(@" AND " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(ToDate), true)));

            }


            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new List<Dictionary<string, object>>();
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new Dictionary<string, object>();
                    obj["CartName"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["VAICNT_ScanName"]);
                    obj["CartId"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VAICNT_InventoryCount_ID"]);
                    obj["TransactionType"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["TransactionType"]);
                    obj["CreatedBy"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj["CartLineCount"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["LineCount"]);
                    obj["TotalCartCount"] = Util.GetValueOfInt(ds.Tables.Count);


                    retDic.Add(obj);
                }
            }



            return retDic;
        }
        // <summary>
        /// VIS0336-using this method for fetching the lines against the cart 
        /// </summary>
        /// <param name="CartId"></param>
        /// <returns></returns>
        public List<Dictionary<string, object>> GetIventoryCartLines(int CartId)
        {
            List<Dictionary<string, object>> retDic = null;
            Dictionary<string, object> obj = null;

            string sql = "SELECT po.VAICNT_InventoryCount_ID,po.VAICNT_InventoryCountLine_ID,po.M_Product_ID,prd.Name AS ProductName, po.C_UOM_ID, u.Name AS UomName, po.UPC, " +
                        " po.M_AttributeSetInstance_ID, ats.Description, po.VAICNT_Quantity, " +
                        " ats.Description FROM VAICNT_InventoryCountLine po LEFT JOIN C_UOM u ON po.C_UOM_ID = u.C_UOM_ID LEFT JOIN M_Product prd" +
                        " ON po.M_Product_ID= prd.M_Product_ID LEFT JOIN M_AttributeSetInstance ats ON po.M_AttributeSetInstance_ID = ats.M_AttributeSetInstance_ID" +
                        " WHERE po.IsActive = 'Y' AND po.VAICNT_InventoryCount_ID = " + CartId + " ORDER BY po.Line";

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new List<Dictionary<string, object>>();
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new Dictionary<string, object>();
                    obj["InventoryCountId"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VAICNT_InventoryCount_ID"]);
                    obj["InventoryCountLineId"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VAICNT_InventoryCountLine_ID"]);
                    obj["ProductId"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]);
                    obj["ProductName"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["ProductName"]);
                    obj["UomId"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_UOM_ID"]);
                    obj["UomName"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["UomName"]);
                    obj["Code"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["UPC"]);
                    obj["AttrId"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_AttributeSetInstance_ID"]);
                    obj["AttrName"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["Description"]);
                    obj["Quantity"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VAICNT_Quantity"]);


                    retDic.Add(obj);
                }
            }
            return retDic;
        }
        // VIS0336- for saving the lines in inventory line tab
        /// </summary>
        /// <param name="InventoryId"></param>
        /// <param name="lstScanDetail"></param>
        /// <param name="IsUpdateTrue"></param>
        /// <returns></returns>
        public string SaveTransactions(Ctx ctx, int InventoryId, List<Inventoryline> lstInventoryLines, bool IsUpdateTrue)

        {
            string msg = "";
            Trx trx = Trx.GetTrx(Trx.CreateTrxName("M_InventoryLine"));
            int InventoryLineID = 0;
            int Org = 0;
            int Client = 0;
            int warehouese = 0;
            string Mdate = "";
            StringBuilder sql = new StringBuilder(); ;
            string InvlinePro = string.Empty;
            InvlinePro = string.Join(",", lstInventoryLines.Select(p => p.ProductId.ToString()));
            int LocatorId = 0;

            bool isContainerApplicable = MTransaction.ProductContainerApplicable(ctx);
            sql.Clear();
            sql.Append("SELECT MovementDate,AD_Org_ID,M_Warehouse_ID,AD_Client_ID FROM M_Inventory WHERE M_Inventory_ID=" + InventoryId);
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                Org = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                Client = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Client_ID"]);
                warehouese = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]);
                Mdate = Util.GetValueOfString(ds.Tables[0].Rows[0]["MovementDate"]);
            }


            LocatorId = MWarehouse.Get(ctx, warehouese).GetDefaultM_Locator_ID();

            if (!isContainerApplicable)
            {
                sql.Clear();
                sql = new StringBuilder(
                @"WITH mt AS (SELECT m_product_id, M_Locator_ID, M_AttributeSetInstance_ID, SUM(CurrentQty) AS CurrentQty FROM
                 (SELECT DISTINCT t.M_Product_ID, t.M_Locator_ID, t.M_AttributeSetInstance_ID, FIRST_VALUE(t.CurrentQty) OVER (PARTITION BY t.M_Product_ID, t.M_AttributeSetInstance_ID, t.M_Locator_ID
                 ORDER BY t.MovementDate DESC, t.M_Transaction_ID DESC) AS CurrentQty FROM m_transaction t INNER JOIN M_Locator l ON t.M_Locator_ID = l.M_Locator_ID
                 WHERE t.MovementDate <= " + GlobalVariable.TO_DATE(Util.GetValueOfDateTime(Mdate), true) +
                @" AND t.AD_Client_ID = " + Client + " AND l.AD_Org_ID = " + Org +
                @" AND l.M_Warehouse_ID = " + warehouese +
                @") t GROUP BY m_product_id, M_Locator_ID, M_AttributeSetInstance_ID )
                 SELECT DISTINCT p.C_UOM_ID,s.M_Product_ID, s.M_Locator_ID, s.M_AttributeSetInstance_ID, mt.currentqty AS Qty, s.QtyOnHand, p.M_AttributeSet_ID FROM M_Product p 
                 INNER JOIN M_Storage s ON (s.M_Product_ID=p.M_Product_ID) INNER JOIN M_Locator l ON (s.M_Locator_ID=l.M_Locator_ID) 
                 JOIN mt ON (mt.M_Product_ID = s.M_Product_ID AND mt.M_Locator_ID = s.M_Locator_ID AND mt.M_AttriButeSetInstance_ID = NVL(s.M_AttriButeSetInstance_ID,0))
                 WHERE l.M_Warehouse_ID = " + warehouese + " AND p.IsActive='Y' AND p.IsStocked='Y' and p.ProductType='I' and p.M_Product_ID IN(" + InvlinePro + ") and l.M_Locator_ID=101");
            }
            else
            {
                sql.Clear();
                sql = new StringBuilder(@"WITH mt AS (SELECT m_product_id, M_Locator_ID, M_AttributeSetInstance_ID, SUM(CurrentQty) AS CurrentQty, M_ProductContainer_ID
                 FROM (SELECT DISTINCT t.M_Product_ID, t.M_Locator_ID, t.M_AttributeSetInstance_ID, NVL(t.M_ProductContainer_ID , 0) AS M_ProductContainer_ID,
                 FIRST_VALUE(t.ContainerCurrentQty) OVER (PARTITION BY t.M_Product_ID, t.M_AttributeSetInstance_ID, t.M_Locator_ID, NVL(t.M_ProductContainer_ID, 0) ORDER BY t.MovementDate DESC, t.M_Transaction_ID DESC) AS CurrentQty
                 FROM m_transaction t INNER JOIN M_Locator l ON t.M_Locator_ID = l.M_Locator_ID 
                 WHERE t.MovementDate <= " + GlobalVariable.TO_DATE(Util.GetValueOfDateTime(Mdate), true) +
           @" AND t.AD_Client_ID = " + Client + " AND l.AD_Org_ID = " + Org +
           @" AND l.M_Warehouse_ID = " + warehouese + @") t GROUP BY m_product_id, M_Locator_ID, M_AttributeSetInstance_ID, M_ProductContainer_ID ) 
                 SELECT DISTINCT p.C_UOM_ID,s.M_Product_ID, s.M_Locator_ID, s.M_AttributeSetInstance_ID, mt.currentqty AS Qty, mt.M_ProductContainer_ID, p.M_AttributeSet_ID FROM M_Product p 
                 INNER JOIN M_ContainerStorage s ON (s.M_Product_ID=p.M_Product_ID) INNER JOIN M_Locator l ON (s.M_Locator_ID=l.M_Locator_ID) 
                 JOIN mt ON (mt.M_Product_ID = s.M_Product_ID AND mt.M_Locator_ID = s.M_Locator_ID AND mt.M_AttriButeSetInstance_ID = NVL(s.M_AttriButeSetInstance_ID,0) AND mt.M_ProductContainer_ID = NVL(s.M_ProductContainer_ID , 0))
                 WHERE l.M_Warehouse_ID = " + warehouese + " AND p.IsActive='Y' AND p.IsStocked='Y' and p.ProductType='I' and p.M_Product_ID IN(" + InvlinePro + ") and l.M_Locator_ID=101");
            }

            DataSet ds2 = DB.ExecuteDataset(sql.ToString(), null, null);
            MInventoryLine inventorline = null;
            decimal? Conqty = 0;
            try
            {
                if (lstInventoryLines != null)
                {

                    sql.Clear();
                    sql.Append("SELECT M_Product_ID, M_AttributeSetInstance_ID,M_InventoryLine_ID FROM M_InventoryLine  WHERE M_Inventory_ID =" + InventoryId);
                    DataSet ds1 = DB.ExecuteDataset(sql.ToString(), null, null);


                    for (int i = 0; i < lstInventoryLines.Count; i++)   //Save InventoryCountLines
                    {

                        InventoryLineID = 0;
                        if (ds1 != null && ds1.Tables[0].Rows.Count > 0)
                        {
                            DataRow[] selectedTable = null;
                            selectedTable = ds1.Tables[0].Select("M_Product_ID=" + lstInventoryLines[i].ProductId + " AND M_AttributeSetInstance_ID=" + lstInventoryLines[i].AttrId);
                            if (selectedTable.Length > 0)
                            {
                                InventoryLineID = Util.GetValueOfInt(selectedTable[0]["M_InventoryLine_ID"]);
                            }
                        }

                        if (InventoryLineID == 0 || (IsUpdateTrue && InventoryLineID > 0))
                        {
                            DataRow[] selectedTable1 = null;
                            selectedTable1 = ds2.Tables[0].Select("M_Product_ID=" + lstInventoryLines[i].ProductId);
                            if (selectedTable1.Length > 0)
                            {
                                if (Util.GetValueOfInt(selectedTable1[0]["C_UOM_ID"]) != lstInventoryLines[i].UOMId)
                                {
                                    Conqty = MUOMConversion.ConvertProductFrom(ctx, lstInventoryLines[i].ProductId, lstInventoryLines[i].UOMId, Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                                }
                                else
                                {
                                    Conqty = Util.GetValueOfDecimal(selectedTable1[0]["Qty"]);
                                }
                            }
                            inventorline = new MInventoryLine(ctx, InventoryLineID, trx);

                            if (InventoryLineID > 0)
                            {
                                inventorline.Set_Value("QtyEntered", lstInventoryLines[i].Qty);
                                inventorline.Set_Value("C_UOM_ID", lstInventoryLines[i].UOMId);
                                inventorline.SetAsOnDateCount(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                                inventorline.Set_Value("VAICNT_InventoryCount_ID", lstInventoryLines[i].InventoryCountId);
                            }
                            else
                            {
                                inventorline.SetM_Inventory_ID(InventoryId);
                                inventorline.SetAD_Org_ID(Org);
                                inventorline.SetAD_Client_ID(Client);
                                inventorline.SetM_Locator_ID(LocatorId);
                                inventorline.SetM_Product_ID(lstInventoryLines[i].ProductId);
                                inventorline.SetM_AttributeSetInstance_ID(lstInventoryLines[i].AttrId);
                                inventorline.Set_Value("C_UOM_ID", lstInventoryLines[i].UOMId);
                                inventorline.SetInventoryType("D");
                                inventorline.SetAdjustmentType("A");
                                inventorline.Set_Value("QtyEntered", lstInventoryLines[i].Qty);
                                inventorline.SetQtyBook(Util.GetValueOfDecimal(Conqty));
                                inventorline.SetAsOnDateCount(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                                inventorline.Set_Value("VAICNT_InventoryCount_ID", lstInventoryLines[i].InventoryCountId);
                            }
                            if (!inventorline.Save(trx))
                            {

                                msg = Msg.GetMsg(ctx, "VA075_ErrorSavingRecord");
                                ValueNamePair pp = VLogger.RetrieveError();
                                if (pp != null && !String.IsNullOrEmpty(pp.GetName()))
                                {
                                    msg += " - " + pp.GetName();
                                }
                                trx.Rollback();
                                trx.Close();

                            }
                        }
                    }

                    if (trx != null)
                    {
                        trx.Commit();
                    }

                }
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                    trx.Close();
                }

                return msg;
            }
            finally
            {
                if (trx != null)
                {
                    trx.Close();
                }
            }


            return msg;
        }

    }
    public class Inventoryline
    {
        public int InventoryCountId { get; set; }
        public string Code { get; set; }
        public string AttributeNo { get; set; }
        public string UOM { get; set; }
        public int ProductId { get; set; }
        public string prodName { get; set; }
        public int UOMId { get; set; }
        public int AttrId { get; set; }
        public string Qty { get; set; }
    }


}