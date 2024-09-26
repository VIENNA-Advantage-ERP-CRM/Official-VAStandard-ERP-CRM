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
        /// VIS0336-set the window ids for fetching and saving records
        /// </summary>
        private enum Windows
        {
            PhysicalInventory = 168,
            Shipment = 169,
            InventoryMove = 170,
            MaterialReceipt = 184,
            InternalUse = 341,
            SalesOrder = 143,
            PurchaseOrder = 181
        }

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
        public List<Dictionary<string, object>> GetIventoryCartData(string CartName, string UserId, string FromDate, string ToDate, string RefNo, int windowID)
        {

            List<Dictionary<string, object>> retDic = null;
            Dictionary<string, object> obj = null;
            StringBuilder sql = new StringBuilder();

            sql.Clear();
            sql.Append("SELECT Name FROM AD_Window WHERE AD_Window_ID=" + windowID);
            string WindowName = Util.GetValueOfString(DB.ExecuteScalar(sql.ToString(), null, null));

            sql.Clear();
            sql.Append("SELECT c.VAICNT_ScanName,c.VAICNT_TransactionType,a.Name,c.VAICNT_InventoryCount_ID, (SELECT NAME FROM AD_Ref_List WHERE AD_Reference_ID=" +
                " (SELECT AD_Reference_ID FROM AD_Reference WHERE Name='VAICNT_TransactionType') AND ISActive='Y' AND Value=VAICNT_TransactionType) AS TransactionType, c.VAICNT_ReferenceNo," +
                " (SELECT COUNT(VAICNT_InventoryCount_ID) FROM VAICNT_InventoryCountLine WHERE VAICNT_InventoryCount_ID=c.VAICNT_InventoryCount_ID) AS LineCount " +
                " FROM VAICNT_InventoryCount  c INNER JOIN AD_User a ON a.AD_User_ID=c.CreatedBy");

            if (windowID == Util.GetValueOfInt(Windows.PhysicalInventory) || WindowName == "VAS_PhysicalInventory") //Inventory count
            {
                sql.Append(" WHERE VAICNT_TransactionType IN ('OT','PI')");
            }

            if (windowID == Util.GetValueOfInt(Windows.InternalUse) || WindowName == "VAS_InternalUseInventory")//Inventory use
            {
                sql.Append(" WHERE VAICNT_TransactionType IN ('OT','IU')");
            }
            if (windowID == Util.GetValueOfInt(Windows.InventoryMove) || WindowName == "VAS_InventoryMove") //Material Transfer
            {
                sql.Append(" WHERE VAICNT_TransactionType IN ('OT','IM')");
            }
            if (windowID == Util.GetValueOfInt(Windows.MaterialReceipt) || WindowName == "VAS_MaterialReceipt")//GRN
            {
                sql.Append(" WHERE VAICNT_TransactionType IN ('OT','MR')");
            }
            if (windowID == Util.GetValueOfInt(Windows.Shipment) || WindowName == "VAS_DeliveryOrder")//shipment/Delivery order
            {
                sql.Append(" WHERE VAICNT_TransactionType IN ('OT','SH')");
            }
            if (windowID == Util.GetValueOfInt(Windows.SalesOrder) || windowID == Util.GetValueOfInt(Windows.PurchaseOrder) || WindowName == "VAS_PurchaseOrder" || WindowName == "VAS_SalesOrder")
            {
                sql.Append(" WHERE VAICNT_TransactionType IN ('OT')");//sales order/purchase order
            }
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
                    obj["ReferenceNo"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["VAICNT_ReferenceNo"]);

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
        public string SaveTransactions(Ctx ctx, int TransactionID, List<Inventoryline> lstInventoryLines, bool IsUpdateTrue, int windowID, string RefNo)

        {
            string msg = "";
            StringBuilder query = new StringBuilder();
            query.Clear();
            query.Append("SELECT Name FROM AD_Window WHERE AD_Window_ID=" + windowID);
            string WindowName = Util.GetValueOfString(DB.ExecuteScalar(query.ToString(), null, null));

            if (windowID == Util.GetValueOfInt(Windows.InternalUse) || WindowName == "VAS_InternalUseInventory")
            {
                msg = SaveInternalUseTransactions(ctx, TransactionID, lstInventoryLines, RefNo);
                return msg;
            }
            else if (windowID == Util.GetValueOfInt(Windows.InventoryMove) || WindowName == "VAS_InventoryMove")
            {
                msg = SaveInventoryMoveTransactions(ctx, TransactionID, lstInventoryLines, RefNo);
                return msg;
            }
            else if (windowID == Util.GetValueOfInt(Windows.MaterialReceipt) || windowID == Util.GetValueOfInt(Windows.Shipment) || WindowName == "VAS_MaterialReceipt" || WindowName == "VAS_DeliveryOrder")
            {
                msg = SaveGRNTransactions(ctx, TransactionID, lstInventoryLines, windowID, RefNo, WindowName);
                return msg;
            }
            else if (windowID == Util.GetValueOfInt(Windows.SalesOrder) || windowID == Util.GetValueOfInt(Windows.PurchaseOrder) || WindowName == "VAS_PurchaseOrder" || WindowName == "VAS_SalesOrder")
            {
                msg = SaveOrderTransactions(ctx, TransactionID, lstInventoryLines);
                return msg;
            }

            else if (windowID == Util.GetValueOfInt(Windows.PhysicalInventory) || WindowName == "VAS_PhysicalInventory")
            {

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
                sql.Append("SELECT MovementDate,AD_Org_ID,M_Warehouse_ID,AD_Client_ID FROM M_Inventory WHERE M_Inventory_ID=" + TransactionID);
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
                        sql.Append("SELECT M_Product_ID, NVL(M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,M_InventoryLine_ID,C_UOM_ID FROM M_InventoryLine  WHERE M_Inventory_ID =" + TransactionID);
                        DataSet ds1 = DB.ExecuteDataset(sql.ToString(), null, null);


                        for (int i = 0; i < lstInventoryLines.Count; i++)   //Save InventoryCountLines
                        {

                            InventoryLineID = 0;
                            if (ds1 != null && ds1.Tables[0].Rows.Count > 0)
                            {
                                DataRow[] selectedTable = null;
                                selectedTable = ds1.Tables[0].Select("M_Product_ID=" + lstInventoryLines[i].ProductId + " AND M_AttributeSetInstance_ID=" + lstInventoryLines[i].AttrId + " AND C_UOM_ID = " + lstInventoryLines[i].UOMId);
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
                                    inventorline.SetAsOnDateCount(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                                    inventorline.Set_Value("VAICNT_InventoryCount_ID", lstInventoryLines[i].InventoryCountId);
                                }
                                else
                                {
                                    inventorline.SetM_Inventory_ID(TransactionID);
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
            }
            return msg;
        }
        /// <summary>
        /// VIS0336-for saving data in inventory use window
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="InventoryId"></param>
        /// <param name="lstInventoryLines"></param>
        /// <param name="RefNo"></param>
        /// <returns>mesg</returns>
        public string SaveInternalUseTransactions(Ctx ctx, int TransactionID, List<Inventoryline> lstInventoryLines, string RefNo)

        {
            StringBuilder sql = new StringBuilder();
            string msg = "";
            Trx trx = Trx.GetTrx(Trx.CreateTrxName("M_InventoryLine"));
            int Org = 0;
            int Client = 0;
            int LocatorId = 0;
            int Warehouse = 0;
            int _charge = 0;
            bool hasReqLines = false;
            DataSet dsReqs = null;
            StringBuilder sbLine = new StringBuilder("");
            StringBuilder sbWhereCond = new StringBuilder("");
            sql.Append("SELECT AD_Client_ID,AD_Org_ID,M_Warehouse_ID FROM M_Inventory WHERE M_Inventory_ID=" + TransactionID);

            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                Org = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                Client = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Client_ID"]);
                Warehouse = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]);
            }

            LocatorId = MWarehouse.Get(ctx, Warehouse).GetDefaultM_Locator_ID();

            if (Util.GetValueOfString(RefNo) != "")
            {
                sql.Clear();
                sql.Append(@"SELECT ol.M_RequisitionLine_ID,  ol.M_Product_ID FROM M_RequisitionLine ol INNER JOIN M_Requisition o
                                    ON ol.M_Requisition_ID =o.M_Requisition_ID WHERE o.Documentno = '" + Util.GetValueOfString(RefNo) + @"' AND ol.M_RequisitionLine_ID NOT IN
                                      (SELECT NVL(M_Requisitionline_ID,0) FROM M_InventoryLine WHERE M_Inventory_ID = " + TransactionID + ")");

                if (Util.GetValueOfString(RefNo) != "")
                {
                    dsReqs = DB.ExecuteDataset(sql.ToString());
                    if (dsReqs != null && dsReqs.Tables[0].Rows.Count > 0)
                        hasReqLines = true;
                }
            }


            try
            {
                if (lstInventoryLines != null)
                {
                    Tuple<String, String, String> mInfo = null;
                    if (Env.HasModulePrefix("DTD001_", out mInfo))
                    {
                        _charge = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_Charge_ID FROM C_Charge WHERE isactive='Y' AND  DTD001_ChargeType='INV'"));
                    }

                    for (int i = 0; i < lstInventoryLines.Count; i++)   //Save InventoryCountLines
                    {

                        MInventoryLine lines = new MInventoryLine(ctx, 0, trx);
                        lines.Set_Value("QtyEntered", Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.SetQtyInternalUse(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.SetAD_Client_ID(Client);
                        lines.SetAD_Org_ID(Org);
                        lines.SetM_Inventory_ID(TransactionID);
                        lines.SetM_Product_ID(lstInventoryLines[i].ProductId);
                        lines.SetM_Locator_ID(LocatorId);
                        lines.Set_Value("QtyEntered", Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.SetIsInternalUse(true);
                        lines.SetQtyInternalUse(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.SetM_AttributeSetInstance_ID(lstInventoryLines[i].AttrId);
                        lines.Set_Value("C_UOM_ID", lstInventoryLines[i].UOMId);
                        lines.SetC_Charge_ID(_charge);
                        if (hasReqLines)
                        {
                            if (sbLine.Length > 0)
                            {
                                sbWhereCond.Clear();
                                sbWhereCond.Append(" AND M_RequisitionLine_ID NOT IN ( " + sbLine + " ) ");
                            }
                            DataRow[] dr = dsReqs.Tables[0].Select(" M_Product_ID = " + Util.GetValueOfInt(lstInventoryLines[i].ProductId) + sbWhereCond);
                            if (dr != null && dr.Length > 0)
                            {
                                int ReqLineID = Util.GetValueOfInt(dr[0]["M_RequisitionLine_ID"]);
                                if (ReqLineID > 0)
                                {
                                    if (sbLine.Length > 0)
                                        sbLine.Append(", " + dr[0]["M_RequisitionLine_ID"]);
                                    else
                                        sbLine.Append(dr[0]["M_RequisitionLine_ID"]);
                                    lines.SetM_RequisitionLine_ID(ReqLineID);
                                }
                            }
                        }

                        if (!lines.Save(trx))
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
        /// <summary>
        /// VIS0336 for saving data in material transfer
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="InventoryId"></param>
        /// <param name="lstInventoryLines"></param>
        /// <param name="RefNo"></param>
        /// <returns>mesg</returns>
        public string SaveInventoryMoveTransactions(Ctx ctx, int TransactionID, List<Inventoryline> lstInventoryLines, string RefNo)

        {
            StringBuilder sql = new StringBuilder();
            string msg = "";
            Trx trx = Trx.GetTrx(Trx.CreateTrxName("M_MovementLine"));
            int Org = 0;
            int Client = 0;
            int ToLocatorId = 0;
            int ToWarehouse = 0;
            int FromWarehouse = 0;
            int FromLocatorId = 0;
            DataSet dsReqs = null;
            bool hasReqLines = false;

            DataSet dsAssets = null;
            bool hasAssets = false;
            StringBuilder sbLine = new StringBuilder("");
            StringBuilder sbWhereCond = new StringBuilder("");
            sql.Append("SELECT AD_Client_ID,AD_Org_ID,M_Warehouse_ID,DTD001_MWarehouseSource_ID FROM M_Movement  WHERE M_Movement_ID=" + TransactionID);

            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                Org = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                Client = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Client_ID"]);
                ToWarehouse = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]);
                FromWarehouse = Util.GetValueOfInt(ds.Tables[0].Rows[0]["DTD001_MWarehouseSource_ID"]);

            }

            ToLocatorId = MWarehouse.Get(ctx, ToWarehouse).GetDefaultM_Locator_ID();
            FromLocatorId = MWarehouse.Get(ctx, FromWarehouse).GetDefaultM_Locator_ID();


            if (Util.GetValueOfString(RefNo) != "")
            {
                sql.Clear();
                sql.Append("SELECT A_Asset_ID, M_Product_ID, NVL(M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID FROM A_Asset WHERE IsActive = 'Y' AND AD_Client_ID = "
                    + ctx.GetAD_Client_ID());
                dsAssets = DB.ExecuteDataset(sql.ToString());
                if (dsAssets != null && dsAssets.Tables[0].Rows.Count > 0)
                    hasAssets = true;
            }


            if (Util.GetValueOfString(RefNo) != "")
            {
                sql.Clear();
                sql.Append(@"SELECT ol.M_RequisitionLine_ID, ol.M_Product_ID FROM M_RequisitionLine ol INNER JOIN M_Requisition o
                                    ON ol.M_Requisition_ID =o.M_Requisition_ID WHERE o.Documentno = '" + Util.GetValueOfString(RefNo) + @"' AND ol.M_RequisitionLine_ID NOT IN
                                      (SELECT NVL(M_Requisitionline_ID,0) FROM M_MovementLine WHERE M_Movement_ID = " + TransactionID + ")");

                if (Util.GetValueOfString(RefNo) != "")
                {
                    dsReqs = DB.ExecuteDataset(sql.ToString());
                    if (dsReqs != null && dsReqs.Tables[0].Rows.Count > 0)
                        hasReqLines = true;
                }
            }

            try
            {
                if (lstInventoryLines != null)
                {


                    for (int i = 0; i < lstInventoryLines.Count; i++)   //Save InventoryCountLines
                    {

                        MMovementLine lines = new MMovementLine(ctx, 0, trx);

                        lines.Set_Value("QtyEntered", Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.SetMovementQty(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));

                        lines.SetAD_Client_ID(Client);
                        lines.SetAD_Org_ID(Org);
                        lines.SetM_Movement_ID(TransactionID);
                        lines.SetM_Product_ID(lstInventoryLines[i].ProductId);
                        lines.Set_Value("QtyEntered", Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.SetMovementQty(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.Set_Value("C_UOM_ID", lstInventoryLines[i].UOMId);
                        lines.SetM_Locator_ID(FromLocatorId);
                        lines.SetM_LocatorTo_ID(ToLocatorId);

                        if (hasReqLines)
                        {
                            if (sbLine.Length > 0)
                            {
                                sbWhereCond.Clear();
                                sbWhereCond.Append(" AND M_RequisitionLine_ID NOT IN ( " + sbLine + " ) ");
                            }
                            DataRow[] dr = dsReqs.Tables[0].Select(" M_Product_ID = " + Util.GetValueOfInt(lstInventoryLines[i].ProductId) + sbWhereCond);
                            if (dr != null && dr.Length > 0)
                            {
                                int ReqLineID = Util.GetValueOfInt(dr[0]["M_RequisitionLine_ID"]);
                                if (ReqLineID > 0)
                                {
                                    if (sbLine.Length > 0)
                                        sbLine.Append(", " + dr[0]["M_RequisitionLine_ID"]);
                                    else
                                        sbLine.Append(dr[0]["M_RequisitionLine_ID"]);
                                    lines.SetM_RequisitionLine_ID(ReqLineID);
                                }
                            }
                        }

                        if (lstInventoryLines[i].AttrId != 0)
                        {
                            lines.SetM_AttributeSetInstance_ID(lstInventoryLines[i].AttrId);
                            if (hasAssets)
                            {
                                DataRow[] drAst = dsAssets.Tables[0].Select(" M_Product_ID = " + lstInventoryLines[i].ProductId + " AND M_AttributeSetInstance_ID = " + lstInventoryLines[i].AttrId);
                                if (drAst != null && drAst.Length > 0)
                                {
                                    if (Util.GetValueOfInt(drAst[0]["A_Asset_ID"]) > 0)
                                        lines.SetA_Asset_ID(Util.GetValueOfInt(drAst[0]["A_Asset_ID"]));
                                }
                            }
                        }

                        if (!lines.Save(trx))
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
        /// <summary>
        /// VIS0336-for saving data in GRN and Shipment
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="InventoryId"></param>
        /// <param name="lstInventoryLines"></param>
        /// <param name="windowID"></param>
        /// <param name="RefNo"></param>
        /// <returns>mesg</returns>
        public string SaveGRNTransactions(Ctx ctx, int TransactionID, List<Inventoryline> lstInventoryLines, int windowID, string RefNo, string WindowName)

        {
            StringBuilder sql = new StringBuilder();
            string msg = "";
            Trx trx = Trx.GetTrx(Trx.CreateTrxName("M_InOutLine"));
            int LocatorId = 0;
            int ordID = 0;
            DataSet dsOrderLines = null;
            bool saved = true;
            bool IsPrintDes = false;
            bool hasOrderLines = false;
            bool hasProdsPurch = false;
            bool hasConversions = false;
            bool fetchedUOMConv = false;
            DataSet dsUOMConv = null;

            MInOut io = new MInOut(ctx, TransactionID, null);
            LocatorId = MWarehouse.Get(ctx, io.GetM_Warehouse_ID()).GetDefaultM_Locator_ID();

            try
            {
                if (lstInventoryLines != null)
                {


                    DataSet dsProPO = GetPurchaingProduct(ctx.GetAD_Client_ID());

                    if (Env.IsModuleInstalled("ED011_"))
                    {
                        if (!fetchedUOMConv)
                        {
                            dsUOMConv = GetUOMConversions(ctx.GetAD_Client_ID());
                            fetchedUOMConv = true;
                        }
                    }

                    if (LocatorId > 0)
                    {

                        sql.Clear();
                        if (RefNo != "")
                        {
                            if (windowID == Util.GetValueOfInt(Windows.Shipment) || WindowName == "VAS_DeliveryOrder")
                                sql.Append("SELECT C_Order_ID FROM C_Order WHERE IsActive = 'Y' AND IsSOTrx= 'Y' AND AD_Client_ID =" + ctx.GetAD_Client_ID() + " AND DocumentNo = '" + RefNo + "'");
                            else if (windowID == Util.GetValueOfInt(Windows.MaterialReceipt) || WindowName == "VAS_MaterialReceipt")
                                sql.Append("SELECT C_Order_ID FROM C_Order WHERE IsActive = 'Y' AND IsSOTrx= 'N' AND AD_Client_ID =" + ctx.GetAD_Client_ID() + " AND DocumentNo = '" + RefNo + "'");
                            else
                                sql.Append("SELECT C_Order_ID FROM C_Order WHERE IsActive = 'Y' AND AD_Client_ID =" + ctx.GetAD_Client_ID() + " AND DocumentNo = '" + RefNo + "'");
                        }
                        else if (windowID == Util.GetValueOfInt(Windows.Shipment) || WindowName == "VAS_DeliveryOrder")
                            sql.Append("SELECT C_Order_ID FROM M_InOut WHERE IsActive = 'Y' AND IsSOTrx = 'Y' AND M_InOut_ID = " + TransactionID);

                        if (sql.Length > 0)
                            ordID = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString()));

                        if (ordID > 0)
                        {
                            string selColumn = "";
                            #region 190 - Check if PrintDescription Column exists

                            int ct = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT COUNT(AD_Column_ID) FROM 
                                                                AD_Column WHERE UPPER(ColumnName)=UPPER('PrintDescription') AND AD_Table_ID=260"));
                            if (ct > 0)
                                IsPrintDes = true;

                            #endregion

                            if (Env.IsModuleInstalled("DTD001_"))
                                selColumn = " , ol.DTD001_Org_ID ";
                            if (IsPrintDes)
                                selColumn += " , ol.PrintDescription ";

                            sql.Clear();
                            sql.Append("SELECT ol.C_OrderLine_ID, ol.M_Product_ID, ol.M_AttributeSetInstance_ID, ol.C_UOM_ID " + selColumn + "  FROM C_OrderLine ol WHERE ol.C_Order_ID = " + ordID);
                            dsOrderLines = DB.ExecuteDataset(sql.ToString());
                            if (dsOrderLines != null && dsOrderLines.Tables[0].Rows.Count > 0)
                            {
                                hasOrderLines = true;
                            }
                        }

                        if (ordID > 0)
                        {
                            io.SetC_Order_ID(ordID);
                            if (!io.Save())
                                saved = false;
                        }
                    }
                    if (saved)
                    {

                        for (int i = 0; i < lstInventoryLines.Count; i++)
                        {

                            MInOutLine lines = new MInOutLine(ctx, 0, trx);

                            lines.SetMovementQty(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                            lines.SetQtyEntered(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));

                            lines.SetAD_Client_ID(io.GetAD_Client_ID());
                            lines.SetAD_Org_ID(io.GetAD_Org_ID());
                            lines.SetM_InOut_ID(TransactionID);
                            lines.SetM_Product_ID(lstInventoryLines[i].ProductId);
                            lines.SetMovementQty(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                            lines.SetQtyEntered(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                            if (hasOrderLines)
                            {
                                DataRow[] drOL = null;
                                if (windowID == Util.GetValueOfInt(Windows.MaterialReceipt) || WindowName == "VAS_MaterialReceipt")
                                {
                                    if (Env.IsModuleInstalled("DTD001_"))
                                        drOL = dsOrderLines.Tables[0].Select(" M_Product_ID = " + Util.GetValueOfInt(lstInventoryLines[i].ProductId) + " AND M_AttributeSetInstance_ID = " + Util.GetValueOfInt(lstInventoryLines[i].AttrId)
                                            + "AND C_UOM_ID = " + Util.GetValueOfInt(lstInventoryLines[i].UOMId) + " AND DTD001_Org_ID = " + ctx.GetAD_Org_ID());
                                }
                                if (!(drOL != null && drOL.Length > 0))
                                {
                                    drOL = dsOrderLines.Tables[0].Select(" M_Product_ID = " + Util.GetValueOfInt(lstInventoryLines[i].ProductId) + " AND M_AttributeSetInstance_ID = " + Util.GetValueOfInt(lstInventoryLines[i].AttrId)
                                        + "AND C_UOM_ID = " + Util.GetValueOfInt(lstInventoryLines[i].UOMId));
                                    if (!(drOL != null && drOL.Length > 0))
                                        drOL = dsOrderLines.Tables[0].Select(" M_Product_ID = " + Util.GetValueOfInt(lstInventoryLines[i].ProductId) + " AND M_AttributeSetInstance_ID = " + Util.GetValueOfInt(lstInventoryLines[i].AttrId)
                                        + "AND C_UOM_ID <> " + Util.GetValueOfInt(lstInventoryLines[i].UOMId));
                                    if (!(drOL != null && drOL.Length > 0))
                                        drOL = dsOrderLines.Tables[0].Select(" M_Product_ID = " + Util.GetValueOfInt(lstInventoryLines[i].ProductId) + " AND M_AttributeSetInstance_ID = " + Util.GetValueOfInt(lstInventoryLines[i].AttrId));
                                    if (!(drOL != null && drOL.Length > 0))
                                        drOL = dsOrderLines.Tables[0].Select(" M_Product_ID = " + Util.GetValueOfInt(lstInventoryLines[i].ProductId));
                                }
                                if (drOL != null && drOL.Length > 0)
                                {
                                    lines.SetC_OrderLine_ID(Util.GetValueOfInt(drOL[0]["C_OrderLine_ID"]));
                                    //190- Set the print description.
                                    if (IsPrintDes)
                                        lines.Set_Value("PrintDescription", Util.GetValueOfString(drOL[0]["PrintDescription"]));
                                }
                            }
                            lines.SetM_Locator_ID(LocatorId);
                            if (Util.GetValueOfInt(lstInventoryLines[i].AttrId) != 0)
                                lines.SetM_AttributeSetInstance_ID(lstInventoryLines[i].AttrId);

                            if (!io.IsSOTrx())
                            {
                                if (dsProPO == null)
                                {
                                    dsProPO = GetPurchaingProduct(ctx.GetAD_Client_ID());
                                    if (dsProPO != null && dsProPO.Tables[0].Rows.Count > 0)
                                        hasProdsPurch = true;
                                }

                                int uomID = Util.GetValueOfInt(lstInventoryLines[i].UOMId);
                                int uom = 0;
                                if (hasProdsPurch)
                                {
                                    DataRow[] dr = dsProPO.Tables[0].Select(" M_Product_ID = " + Util.GetValueOfInt(lstInventoryLines[i].ProductId) + " AND C_BPartner_ID = " + io.GetC_BPartner_ID());
                                    if (dr != null && dr.Length > 0)
                                        uom = Util.GetValueOfInt(dr[0]["C_UOM_ID"]);
                                }

                                if (uomID != 0)
                                {
                                    if (uomID != uom && uom != 0)
                                    {
                                        if (!fetchedUOMConv)
                                        {
                                            dsUOMConv = GetUOMConversions(ctx.GetAD_Client_ID());
                                            fetchedUOMConv = true;
                                            if (dsUOMConv != null && dsUOMConv.Tables[0].Rows.Count > 0)
                                                hasConversions = true;
                                        }

                                        if (hasConversions)
                                        {
                                            Decimal? Res = 0;
                                            DataRow[] drConv = dsUOMConv.Tables[0].Select(" C_UOM_ID = " + uomID + " AND C_UOM_To_ID = " + uom + " AND M_Product_ID= " + Util.GetValueOfInt(lstInventoryLines[i].ProductId));
                                            if (drConv != null && drConv.Length > 0)
                                            {
                                                Res = Util.GetValueOfDecimal(drConv[0]["MultiplyRate"]);
                                                if (Res <= 0)
                                                {
                                                    drConv = dsUOMConv.Tables[0].Select(" C_UOM_ID = " + uomID + " AND C_UOM_To_ID = " + uom);
                                                    if (drConv != null && drConv.Length > 0)
                                                        Res = Util.GetValueOfDecimal(drConv[0]["MultiplyRate"]);
                                                }
                                            }

                                            if (Res > 0)
                                                lines.Set_Value("QtyEntered", Util.GetValueOfDecimal(lstInventoryLines[i].Qty) * Res);

                                        }
                                        lines.SetC_UOM_ID(uom);
                                    }
                                    else
                                        lines.SetC_UOM_ID(uomID);
                                }
                            }
                            else
                                lines.SetC_UOM_ID(lstInventoryLines[i].UOMId);

                            if (!lines.Save(trx))
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

                        if (trx != null)
                        {
                            trx.Commit();
                        }
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
        /// <summary>
        /// VIS0336-for saving data in sales order/purchase order
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="InventoryId"></param>
        /// <param name="lstInventoryLines"></param>
        /// <returns></returns>
        public string SaveOrderTransactions(Ctx ctx, int TransactionID, List<Inventoryline> lstInventoryLines)

        {
            StringBuilder sql = new StringBuilder();
            string msg = "";
            Trx trx = Trx.GetTrx(Trx.CreateTrxName("C_OrderLine"));
            int Org = 0;
            int Client = 0;
            int BPartner = 0;
            bool IsSOTrx = false;
            DataSet dsProPO = null;
            bool hasProdsPurch = false;
            int PriceList = 0;
            sql.Append("SELECT AD_Client_ID,AD_Org_ID,M_Warehouse_ID,IsSOTrx,C_BPartner_ID,M_PriceList_ID FROM C_Order WHERE C_Order_ID=" + TransactionID);

            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                Org = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                Client = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Client_ID"]);
                BPartner = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_BPartner_ID"]);
                PriceList = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_PriceList_ID"]);
                if (Util.GetValueOfString(ds.Tables[0].Rows[0]["IsSOTrx"]) == "Y")
                {
                    IsSOTrx = true;

                }
            }

            if (!IsSOTrx)
            {
                if (dsProPO == null)
                {
                    dsProPO = GetPurchaingProduct(ctx.GetAD_Client_ID());
                    if (dsProPO != null && dsProPO.Tables[0].Rows.Count > 0)
                        hasProdsPurch = true;
                }
            }
            try
            {
                if (lstInventoryLines != null)
                {
                    for (int i = 0; i < lstInventoryLines.Count; i++)   //Save InventoryCountLines
                    {
                        MOrderLine lines = new MOrderLine(ctx, 0, trx);
                        lines.SetAD_Client_ID(Client);
                        lines.SetAD_Org_ID(Org);
                        lines.SetM_Product_ID(lstInventoryLines[i].ProductId);
                        lines.SetQtyEntered(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.SetQtyOrdered(Util.GetValueOfDecimal(lstInventoryLines[i].Qty));
                        lines.SetC_Order_ID(TransactionID);

                        if (lstInventoryLines[i].AttrId != 0)
                            lines.SetM_AttributeSetInstance_ID(lstInventoryLines[i].AttrId);

                        if (IsSOTrx)
                        {
                            int uomID = Util.GetValueOfInt(lstInventoryLines[i].UOMId);
                            int uom = 0;

                            if (hasProdsPurch)
                            {
                                DataRow[] dr = dsProPO.Tables[0].Select(" M_Product_ID = " + lstInventoryLines[i].ProductId + " AND C_BPartner_ID = " + BPartner);
                                if (dr != null && dr.Length > 0)
                                    uom = Util.GetValueOfInt(dr[0]["C_UOM_ID"]);
                            }

                            if (uomID != 0)
                            {
                                if (uomID != uom && uom != 0)
                                    lines.SetC_UOM_ID(uom);
                                else
                                    lines.SetC_UOM_ID(uomID);

                            }
                        }
                        else
                            lines.SetC_UOM_ID(lstInventoryLines[i].UOMId);

                        lines.SetPrice(PriceList);
                        if (!lines.Save(trx))
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

        public DataSet GetPurchaingProduct(int AD_Client_ID)
        {
            StringBuilder _sqlQuery = new StringBuilder();
            DataSet dsProPurch = null;
            _sqlQuery.Clear();
            _sqlQuery.Append(@"SELECT vdr.C_UOM_ID, vdr.C_BPartner_ID, p.M_Product_ID FROM M_Product p LEFT JOIN 
                            M_Product_Po vdr ON p.M_Product_ID= vdr.M_Product_ID WHERE p.AD_Client_ID = " + AD_Client_ID);
            dsProPurch = DB.ExecuteDataset(_sqlQuery.ToString());
            return dsProPurch;
        }
        /// <summary>
        /// VIS0336-conversion method
        /// </summary>
        /// <param name="AD_Client_ID"></param>
        /// <returns></returns>
        public DataSet GetUOMConversions(int AD_Client_ID)
        {
            StringBuilder _sqlQuery = new StringBuilder();
            DataSet dsConvs = null;
            _sqlQuery.Clear();
            _sqlQuery.Append(@"SELECT con.DivideRate, TRUNC(con.multiplyrate,4) AS MultiplyRate, con.C_UOM_ID, con.C_UOM_To_ID, con.M_Product_ID FROM C_UOM_Conversion con INNER JOIN C_UOM uom ON con.C_UOM_ID = uom.C_UOM_ID WHERE con.IsActive = 'Y' AND con.AD_Client_ID = " + AD_Client_ID);
            dsConvs = DB.ExecuteDataset(_sqlQuery.ToString());
            return dsConvs;
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