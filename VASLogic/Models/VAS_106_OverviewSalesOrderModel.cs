/// <summary>
/// Module Name : VASLogic
/// Purpose     : Sales Order Overview tab panel data (read side) + two write
///               actions (Complete Sales Order, Create Contract from a line).
///               Returns header identity, customer + addresses, created-from
///               origin documents, order lines (with contract flag), delivery
///               readiness, deliveries, invoices, a merged activity timeline
///               and notes for a selected sales order (C_Order, IsSOTrx = 'Y').
///               Consumed by the VAS.VAS_106_OverviewSalesOrder tab panel.
///
///               Schema note: every table / column used here is verified to
///               exist in the platform model (C_Order, C_OrderLine, C_Contract,
///               VAS_Opportunity, M_InOut, C_Invoice, M_Storage, etc.). No
///               columns are invented. SQL avoids Oracle-only NVL / TRUNC /
///               TO_CHAR and PostgreSQL-only constructs (COALESCE + CASE only)
///               so a single query runs on both databases; date / currency
///               formatting is done client-side.
/// Chronological development:
///   VAI163   2026-07-08  Created
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_106_OverviewSalesOrderModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_106_OverviewSalesOrderModel).FullName);

        // ================================================================= //
        //  READ SIDE                                                        //
        // ================================================================= //

        /// <summary>
        /// Returns the full overview payload for the selected sales order.
        /// MRole access filtering is applied only on the primary physical table
        /// (C_Order alias "o"); child queries inherit the parent's authorization.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <returns>Populated <see cref="SalesOrderOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public SalesOrderOverviewData GetSalesOrderOverview(Ctx ctx, int C_Order_ID)
        {
            SalesOrderOverviewData result = new SalesOrderOverviewData();
            if (C_Order_ID <= 0) return result;

            string sql = @"SELECT
                              o.C_Order_ID,
                              o.DocumentNo,
                              o.POReference,
                              o.DateOrdered,
                              o.DatePromised,
                              o.DocStatus,
                              o.Posted,
                              o.PriorityRule,
                              o.Created,
                              o.GrandTotal,
                              o.TotalLines,
                              o.C_Currency_ID,
                              o.C_BPartner_ID,
                              o.C_BPartner_Location_ID   AS ShipLocationId,
                              o.Bill_Location_ID         AS BillLocationId,
                              o.AD_User_ID               AS ContactId,
                              o.DeliveryRule,
                              o.InvoiceRule,
                              bp.Name                    AS CustomerName,
                              bp.SOCreditStatus          AS CreditStatus,
                              contact.Name               AS ContactName,
                              contact.Phone              AS ContactPhone,
                              contact.EMail              AS ContactEmail,
                              sr.Name                    AS SalesRepName,
                              pt.Name                    AS PaymentTermName,
                              pl.Name                    AS PriceListName,
                              wh.Name                    AS WarehouseName,
                              cur.CurSymbol              AS CurSymbol,
                              cur.ISO_Code               AS ISO_Code,
                              cur.StdPrecision           AS StdPrecision
                            FROM C_Order o
                            INNER JOIN C_BPartner bp        ON (o.C_BPartner_ID   = bp.C_BPartner_ID)
                            LEFT OUTER JOIN AD_User contact  ON (o.AD_User_ID      = contact.AD_User_ID)
                            LEFT OUTER JOIN AD_User sr        ON (o.SalesRep_ID     = sr.AD_User_ID)
                            LEFT OUTER JOIN C_PaymentTerm pt  ON (o.C_PaymentTerm_ID = pt.C_PaymentTerm_ID)
                            LEFT OUTER JOIN M_PriceList pl    ON (o.M_PriceList_ID  = pl.M_PriceList_ID)
                            LEFT OUTER JOIN M_Warehouse wh    ON (o.M_Warehouse_ID  = wh.M_Warehouse_ID)
                            INNER JOIN C_Currency cur         ON (o.C_Currency_ID   = cur.C_Currency_ID)
                            WHERE o.C_Order_ID = @C_Order_ID
                              AND o.IsActive   = 'Y'
                              AND o.IsSOTrx    = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.C_Order_ID   = Util.GetValueOfInt(r["C_Order_ID"]);
            result.DocumentNo   = Util.GetValueOfString(r["DocumentNo"]);
            result.POReference  = Util.GetValueOfString(r["POReference"]);
            result.DateOrdered  = Util.GetValueOfDateTime(r["DateOrdered"]);
            result.DatePromised = Util.GetValueOfDateTime(r["DatePromised"]);
            result.DocStatus    = Util.GetValueOfString(r["DocStatus"]);
            result.Posted       = Util.GetValueOfString(r["Posted"]);
            result.PriorityRule = Util.GetValueOfString(r["PriorityRule"]);
            result.Created      = Util.GetValueOfDateTime(r["Created"]);

            result.GrandTotal   = Util.GetValueOfDecimal(r["GrandTotal"]);
            result.TotalLines   = Util.GetValueOfDecimal(r["TotalLines"]);
            result.TaxAmt       = result.GrandTotal - result.TotalLines;

            result.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            result.CurSymbol     = Util.GetValueOfString(r["CurSymbol"]);
            result.ISO_Code      = Util.GetValueOfString(r["ISO_Code"]);
            result.StdPrecision  = Util.GetValueOfInt(r["StdPrecision"]);

            result.C_BPartner_ID  = Util.GetValueOfInt(r["C_BPartner_ID"]);
            result.CustomerName   = Util.GetValueOfString(r["CustomerName"]);
            result.CreditStatus   = Util.GetValueOfString(r["CreditStatus"]);
            result.ContactName    = Util.GetValueOfString(r["ContactName"]);
            result.ContactPhone   = Util.GetValueOfString(r["ContactPhone"]);
            result.ContactEmail   = Util.GetValueOfString(r["ContactEmail"]);
            result.SalesRepName   = Util.GetValueOfString(r["SalesRepName"]);
            result.PaymentTermName = Util.GetValueOfString(r["PaymentTermName"]);
            result.PriceListName  = Util.GetValueOfString(r["PriceListName"]);
            result.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);
            result.DeliveryRule   = Util.GetValueOfString(r["DeliveryRule"]);
            result.InvoiceRule    = Util.GetValueOfString(r["InvoiceRule"]);

            int shipLocId = Util.GetValueOfInt(r["ShipLocationId"]);
            int billLocId = Util.GetValueOfInt(r["BillLocationId"]);

            // ----- Child data -----
            LoadAddresses(shipLocId, billLocId, result);
            LoadCreatedFrom(C_Order_ID, result);
            result.Lines            = LoadLines(C_Order_ID);
            result.DeliveryReadiness = LoadDeliveryReadiness(C_Order_ID);
            result.Deliveries       = LoadDeliveries(C_Order_ID);
            result.Invoices         = LoadInvoices(C_Order_ID);
            result.Activity         = LoadActivity(C_Order_ID);
            result.Notes            = LoadNotes(C_Order_ID);
            result.Frequencies      = LoadFrequencies(ctx);

            return result;
        }

        /// <summary>
        /// Loads the ship-to and bill-to addresses (C_BPartner_Location +
        /// C_Location) and composes each into a single display string.
        /// </summary>
        private void LoadAddresses(int shipLocationId, int billLocationId, SalesOrderOverviewData d)
        {
            if (shipLocationId <= 0 && billLocationId <= 0) return;
            try
            {
                string sql = @"SELECT
                                  bpl.C_BPartner_Location_ID AS LocId,
                                  loc.Address1 AS Address1,
                                  loc.Address2 AS Address2,
                                  loc.City     AS City,
                                  loc.Postal   AS Postal,
                                  reg.Name     AS RegionName,
                                  ctry.Name    AS CountryName
                                FROM C_BPartner_Location bpl
                                LEFT OUTER JOIN C_Location loc  ON (loc.C_Location_ID = bpl.C_Location_ID)
                                LEFT OUTER JOIN C_Country ctry  ON (loc.C_Country_ID  = ctry.C_Country_ID)
                                LEFT OUTER JOIN C_Region reg    ON (loc.C_Region_ID   = reg.C_Region_ID)
                                WHERE bpl.IsActive = 'Y'
                                  AND bpl.C_BPartner_Location_ID IN (@ship, @bill)";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ship", shipLocationId),
                    new SqlParameter("@bill", billLocationId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int locId = Util.GetValueOfInt(r["LocId"]);
                    string addr = BuildAddress(
                        Util.GetValueOfString(r["Address1"]),
                        Util.GetValueOfString(r["Address2"]),
                        Util.GetValueOfString(r["City"]),
                        Util.GetValueOfString(r["RegionName"]),
                        Util.GetValueOfString(r["Postal"]),
                        Util.GetValueOfString(r["CountryName"]));
                    if (locId == shipLocationId) d.ShipToAddress = addr;
                    if (locId == billLocationId) d.BillToAddress = addr;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadAddresses (C_Order_ID=" + d.C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads the origin documents the order was created from: quotation
        /// (C_Order.C_Order_Quotation), opportunity (C_Order.VAS_Opportunity_ID
        /// -> VAS_Opportunity.Name) and project (C_Order.C_Project_ID ->
        /// C_Project.Name). Each source column is module-optional, so the whole
        /// block is guarded — a missing column degrades to "no created-from"
        /// rather than breaking the overview. C_ProjectRef_ID is intentionally
        /// NOT used.
        /// </summary>
        private void LoadCreatedFrom(int C_Order_ID, SalesOrderOverviewData d)
        {
            try
            {
                string sql = @"SELECT
                                  q.C_Order_ID    AS QuotationId,
                                  q.DocumentNo    AS QuotationNo,
                                  o.VAS_Opportunity_ID AS OpportunityId,
                                  opp.Name        AS OpportunityName,
                                  proj.C_Project_ID AS ProjectId,
                                  proj.Name       AS ProjectName
                                FROM C_Order o
                                LEFT OUTER JOIN C_Order q
                                       ON (q.C_Order_ID = o.C_Order_Quotation AND q.IsActive = 'Y')
                                LEFT OUTER JOIN VAS_Opportunity opp
                                       ON (opp.VAS_Opportunity_ID = o.VAS_Opportunity_ID AND opp.IsActive = 'Y')
                                LEFT OUTER JOIN C_Project proj
                                       ON (proj.C_Project_ID = o.C_Project_ID AND proj.IsActive = 'Y')
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.QuotationId    = Util.GetValueOfInt(r["QuotationId"]);
                d.QuotationNo    = Util.GetValueOfString(r["QuotationNo"]);
                d.OpportunityId  = Util.GetValueOfInt(r["OpportunityId"]);
                d.OpportunityName = Util.GetValueOfString(r["OpportunityName"]);
                d.ProjectId      = Util.GetValueOfInt(r["ProjectId"]);
                d.ProjectName    = Util.GetValueOfString(r["ProjectName"]);
            }
            catch (Exception ex)
            {
                // A deployment without the VAS_Opportunity_ID / C_Order_Quotation
                // module column reaches here; keep the overview working.
                _log.Severe("LoadCreatedFrom (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads C_OrderLine rows for the order with product / charge metadata,
        /// UOM symbol, the line-level contract flag (C_OrderLine.IsContract) and
        /// any created contract (C_OrderLine.C_Contract_ID -> C_Contract).
        /// </summary>
        private List<SalesOrderLineData> LoadLines(int C_Order_ID)
        {
            List<SalesOrderLineData> lines = new List<SalesOrderLineData>();
            try
            {
                string sql = @"SELECT
                                  ol.C_OrderLine_ID,
                                  ol.Line,
                                  COALESCE(ol.QtyOrdered, 0)   AS QtyOrdered,
                                  COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                                  COALESCE(ol.QtyInvoiced, 0)  AS QtyInvoiced,
                                  COALESCE(ol.PriceActual, 0)  AS PriceActual,
                                  COALESCE(ol.Discount, 0)     AS Discount,
                                  COALESCE(ol.LineNetAmt, 0)   AS LineNetAmt,
                                  ol.Description               AS LineDescription,
                                  ol.M_Product_ID,
                                  ol.C_Charge_ID,
                                  ol.IsContract,
                                  ol.C_Contract_ID,
                                  p.Value        AS ProductValue,
                                  p.Name         AS ProductName,
                                  p.ProductType  AS ProductType,
                                  ch.Name        AS ChargeName,
                                  uom.UOMSymbol  AS UOMSymbol,
                                  COALESCE(uom.StdPrecision, 0) AS UOMPrecision,
                                  con.DocumentNo AS ContractNo
                                FROM C_OrderLine ol
                                LEFT OUTER JOIN M_Product  p   ON (ol.M_Product_ID = p.M_Product_ID)
                                LEFT OUTER JOIN C_Charge   ch  ON (ol.C_Charge_ID  = ch.C_Charge_ID)
                                LEFT OUTER JOIN C_UOM      uom ON (ol.C_UOM_ID     = uom.C_UOM_ID)
                                LEFT OUTER JOIN C_Contract con ON (ol.C_Contract_ID = con.C_Contract_ID)
                                WHERE ol.C_Order_ID = @C_Order_ID
                                  AND ol.IsActive   = 'Y'
                                ORDER BY ol.Line";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return lines;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    SalesOrderLineData ln = new SalesOrderLineData();
                    ln.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                    ln.Line           = Util.GetValueOfInt(r["Line"]);
                    ln.QtyOrdered     = Util.GetValueOfDecimal(r["QtyOrdered"]);
                    ln.QtyDelivered   = Util.GetValueOfDecimal(r["QtyDelivered"]);
                    ln.QtyInvoiced    = Util.GetValueOfDecimal(r["QtyInvoiced"]);
                    ln.PriceActual    = Util.GetValueOfDecimal(r["PriceActual"]);
                    ln.Discount       = Util.GetValueOfDecimal(r["Discount"]);
                    ln.LineNetAmt     = Util.GetValueOfDecimal(r["LineNetAmt"]);
                    ln.Description    = Util.GetValueOfString(r["LineDescription"]);
                    ln.M_Product_ID   = Util.GetValueOfInt(r["M_Product_ID"]);
                    ln.C_Charge_ID    = Util.GetValueOfInt(r["C_Charge_ID"]);
                    ln.IsContractFlag = Util.GetValueOfString(r["IsContract"]) == "Y";
                    ln.C_Contract_ID  = Util.GetValueOfInt(r["C_Contract_ID"]);
                    ln.ContractNo     = Util.GetValueOfString(r["ContractNo"]);
                    ln.ProductValue   = Util.GetValueOfString(r["ProductValue"]);
                    ln.ProductName    = Util.GetValueOfString(r["ProductName"]);
                    ln.ProductType    = Util.GetValueOfString(r["ProductType"]);
                    ln.ChargeName     = Util.GetValueOfString(r["ChargeName"]);
                    ln.UOMSymbol      = Util.GetValueOfString(r["UOMSymbol"]);
                    ln.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);

                    if (string.IsNullOrEmpty(ln.ProductName) && !string.IsNullOrEmpty(ln.ChargeName))
                        ln.ProductName = ln.ChargeName;

                    // Line family: product (stockable) / service / charge.
                    if (ln.C_Charge_ID > 0)
                        ln.LineType = "charge";
                    else if (ln.ProductType == "S")
                        ln.LineType = "service";
                    else if (ln.ProductType == "I")
                        ln.LineType = "product";
                    else
                        ln.LineType = "other";

                    // Delivered state (stockable lines only surface a bar).
                    if (ln.QtyOrdered > 0 && ln.QtyDelivered >= ln.QtyOrdered)
                        ln.DeliveredState = "full";
                    else if (ln.QtyDelivered > 0)
                        ln.DeliveredState = "part";
                    else
                        ln.DeliveredState = "none";

                    lines.Add(ln);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadLines (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return lines;
        }

        /// <summary>
        /// Loads delivery readiness for the pending stockable lines only
        /// (ProductType = 'I'): pending-to-deliver (QtyOrdered - QtyDelivered)
        /// against on-hand stock (SUM M_Storage.QtyOnHand) in the fulfilment
        /// warehouse (line warehouse, else order warehouse). Service / charge
        /// lines are excluded. On Hand only — no available-to-promise.
        /// </summary>
        private List<DeliveryReadinessData> LoadDeliveryReadiness(int C_Order_ID)
        {
            List<DeliveryReadinessData> rows = new List<DeliveryReadinessData>();
            try
            {
                string sql = @"SELECT
                                  ol.C_OrderLine_ID,
                                  p.M_Product_ID,
                                  p.Value AS ProductValue,
                                  p.Name  AS ProductName,
                                  wh.Name AS WarehouseName,
                                  COALESCE(ol.QtyOrdered, 0)   AS QtyOrdered,
                                  COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                                  COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) AS PendingQty,
                                  COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) AS QtyOnHand
                                FROM C_Order o
                                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID)
                                INNER JOIN M_Product p     ON (p.M_Product_ID = ol.M_Product_ID)
                                LEFT OUTER JOIN M_Warehouse wh
                                       ON (wh.M_Warehouse_ID = COALESCE(ol.M_Warehouse_ID, o.M_Warehouse_ID))
                                LEFT OUTER JOIN M_Locator loc
                                       ON (loc.M_Warehouse_ID = wh.M_Warehouse_ID AND loc.IsActive = 'Y')
                                LEFT OUTER JOIN M_Storage s
                                       ON (s.M_Locator_ID = loc.M_Locator_ID
                                           AND s.M_Product_ID = ol.M_Product_ID
                                           AND s.IsActive = 'Y')
                                WHERE o.C_Order_ID = @C_Order_ID
                                  AND o.IsActive   = 'Y'
                                  AND o.IsSOTrx    = 'Y'
                                  AND ol.IsActive  = 'Y'
                                  AND p.ProductType = 'I'
                                GROUP BY ol.C_OrderLine_ID, p.M_Product_ID, p.Value, p.Name, wh.Name,
                                         COALESCE(ol.QtyOrdered, 0), COALESCE(ol.QtyDelivered, 0)
                                ORDER BY ol.C_OrderLine_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    DeliveryReadinessData rd = new DeliveryReadinessData();
                    rd.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                    rd.ProductValue   = Util.GetValueOfString(r["ProductValue"]);
                    rd.ProductName    = Util.GetValueOfString(r["ProductName"]);
                    rd.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);
                    rd.PendingQty     = Util.GetValueOfDecimal(r["PendingQty"]);
                    rd.QtyOnHand      = Util.GetValueOfDecimal(r["QtyOnHand"]);

                    if (rd.PendingQty <= 0)                    rd.Readiness = "ready";     // fully delivered
                    else if (rd.QtyOnHand >= rd.PendingQty)    rd.Readiness = "instock";   // can ship now
                    else if (rd.QtyOnHand > 0)                 rd.Readiness = "short";      // partial cover
                    else                                       rd.Readiness = "awaited";    // nothing on hand
                    rows.Add(rd);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDeliveryReadiness (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return rows;
        }

        /// <summary>
        /// Loads delivery orders (M_InOut, IsSOTrx = 'Y') linked to the sales
        /// order, excluding reversed / voided, with a line count and total
        /// movement quantity.
        /// </summary>
        private List<DeliveryData> LoadDeliveries(int C_Order_ID)
        {
            List<DeliveryData> rows = new List<DeliveryData>();
            try
            {
                string sql = @"SELECT
                                  io.M_InOut_ID,
                                  io.DocumentNo,
                                  io.DocStatus,
                                  io.MovementDate,
                                  io.TrackingNo,
                                  wh.Name AS WarehouseName,
                                  COALESCE(SUM(COALESCE(iol.MovementQty, 0)), 0) AS DeliveredQty,
                                  COUNT(iol.M_InOutLine_ID) AS LineCount
                                FROM M_InOut io
                                LEFT OUTER JOIN M_InOutLine iol
                                       ON (iol.M_InOut_ID = io.M_InOut_ID AND iol.IsActive = 'Y')
                                LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID = io.M_Warehouse_ID)
                                WHERE io.C_Order_ID = @C_Order_ID
                                  AND io.IsActive   = 'Y'
                                  AND io.IsSOTrx    = 'Y'
                                  AND io.DocStatus NOT IN ('RE', 'VO')
                                GROUP BY io.M_InOut_ID, io.DocumentNo, io.DocStatus, io.MovementDate,
                                         io.TrackingNo, wh.Name
                                ORDER BY io.MovementDate DESC, io.DocumentNo DESC";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    DeliveryData dv = new DeliveryData();
                    dv.M_InOut_ID    = Util.GetValueOfInt(r["M_InOut_ID"]);
                    dv.DocumentNo    = Util.GetValueOfString(r["DocumentNo"]);
                    dv.DocStatus     = Util.GetValueOfString(r["DocStatus"]);
                    dv.MovementDate  = Util.GetValueOfDateTime(r["MovementDate"]);
                    dv.TrackingNo    = Util.GetValueOfString(r["TrackingNo"]);
                    dv.WarehouseName = Util.GetValueOfString(r["WarehouseName"]);
                    dv.DeliveredQty  = Util.GetValueOfDecimal(r["DeliveredQty"]);
                    dv.LineCount     = Util.GetValueOfInt(r["LineCount"]);
                    rows.Add(dv);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDeliveries (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return rows;
        }

        /// <summary>
        /// Loads invoices (C_Invoice, IsSOTrx = 'Y') linked to the sales order,
        /// excluding reversed / voided.
        /// </summary>
        private List<InvoiceData> LoadInvoices(int C_Order_ID)
        {
            List<InvoiceData> rows = new List<InvoiceData>();
            try
            {
                string sql = @"SELECT
                                  inv.C_Invoice_ID,
                                  inv.DocumentNo,
                                  inv.DocStatus,
                                  inv.DateInvoiced,
                                  COALESCE(inv.GrandTotal, 0) AS GrandTotal,
                                  inv.IsPaid
                                FROM C_Invoice inv
                                WHERE inv.C_Order_ID = @C_Order_ID
                                  AND inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'Y'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')
                                ORDER BY inv.DateInvoiced DESC, inv.DocumentNo DESC";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    InvoiceData iv = new InvoiceData();
                    iv.C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]);
                    iv.DocumentNo   = Util.GetValueOfString(r["DocumentNo"]);
                    iv.DocStatus    = Util.GetValueOfString(r["DocStatus"]);
                    iv.DateInvoiced = Util.GetValueOfDateTime(r["DateInvoiced"]);
                    iv.GrandTotal   = Util.GetValueOfDecimal(r["GrandTotal"]);
                    iv.IsPaid       = Util.GetValueOfString(r["IsPaid"]) == "Y";
                    rows.Add(iv);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadInvoices (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return rows;
        }

        /// <summary>
        /// Builds the activity timeline by merging chat notes (CM_ChatEntry),
        /// deliveries (M_InOut), invoices (C_Invoice) and the order's own
        /// create / confirm milestones (C_Order), newest-first. Each source is
        /// guarded so a DB-level issue with one degrades to a partial feed.
        /// (AppointmentsInfo / R_Request are intentionally not joined here —
        /// no verified direct C_Order link exists for them.)
        /// </summary>
        private List<ActivityData> LoadActivity(int C_Order_ID)
        {
            const int MAX_ENTRIES = 15;
            List<ActivityData> activity = new List<ActivityData>();

            LoadNoteActivity(C_Order_ID, activity);
            LoadDeliveryActivity(C_Order_ID, activity);
            LoadInvoiceActivity(C_Order_ID, activity);
            LoadOrderMilestoneActivity(C_Order_ID, activity);

            activity.Sort((a, b) =>
                b.EventTime.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.EventTime.GetValueOrDefault(DateTime.MinValue)));

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        private void LoadNoteActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT ce.CharacterData, ce.Created, u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                 INNER JOIN CM_Chat ch     ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u ON (ce.AD_User_ID = u.AD_User_ID)
                                WHERE ch.AD_Table_ID =
                                      (SELECT t.AD_Table_ID FROM AD_Table t WHERE t.TableName = 'C_Order')
                                  AND ch.Record_ID = @C_Order_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType   = "Note",
                        Title       = Util.GetValueOfString(r["CharacterData"]),
                        ActorName   = Util.GetValueOfString(r["UserName"]),
                        EventTime   = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        private void LoadDeliveryActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT io.DocumentNo, io.DocStatus, io.Updated, u.Name AS UserName
                                 FROM M_InOut io
                                 LEFT OUTER JOIN AD_User u ON (io.UpdatedBy = u.AD_User_ID)
                                WHERE io.C_Order_ID = @C_Order_ID
                                  AND io.IsActive   = 'Y'
                                  AND io.IsSOTrx    = 'Y'
                                  AND io.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType  = "Delivery",
                        Title      = Util.GetValueOfString(r["DocumentNo"]),
                        ActorName  = Util.GetValueOfString(r["UserName"]),
                        EventTime  = Util.GetValueOfDateTime(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDeliveryActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        private void LoadInvoiceActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT inv.DocumentNo, COALESCE(inv.GrandTotal, 0) AS GrandTotal,
                                      inv.Updated, u.Name AS UserName
                                 FROM C_Invoice inv
                                 LEFT OUTER JOIN AD_User u ON (inv.UpdatedBy = u.AD_User_ID)
                                WHERE inv.C_Order_ID = @C_Order_ID
                                  AND inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'Y'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType  = "Invoice",
                        Title      = Util.GetValueOfString(r["DocumentNo"]),
                        Amount     = Util.GetValueOfDecimal(r["GrandTotal"]),
                        ActorName  = Util.GetValueOfString(r["UserName"]),
                        EventTime  = Util.GetValueOfDateTime(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadInvoiceActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        private void LoadOrderMilestoneActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT o.Created, o.Updated, o.DocStatus, o.DocumentNo,
                                      cu.Name AS CreatedByName, uu.Name AS UpdatedByName
                                 FROM C_Order o
                                 LEFT OUTER JOIN AD_User cu ON (o.CreatedBy = cu.AD_User_ID)
                                 LEFT OUTER JOIN AD_User uu ON (o.UpdatedBy = uu.AD_User_ID)
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new ActivityData
                {
                    EventType = "Created",
                    Title     = Util.GetValueOfString(r["DocumentNo"]),
                    ActorName = Util.GetValueOfString(r["CreatedByName"]),
                    EventTime = Util.GetValueOfDateTime(r["Created"])
                });

                string docStatus = Util.GetValueOfString(r["DocStatus"]);
                if (docStatus == "CO" || docStatus == "CL")
                {
                    list.Add(new ActivityData
                    {
                        EventType = "Updated",
                        Title     = Util.GetValueOfString(r["DocumentNo"]),
                        ActorName = Util.GetValueOfString(r["UpdatedByName"]),
                        EventTime = Util.GetValueOfDateTime(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadOrderMilestoneActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads notes: the order header note (C_Order.Description) plus each
        /// line's own note (product / charge name + line description). Composed
        /// in C# so the SQL stays portable (no DB-specific string functions).
        /// </summary>
        private List<NoteData> LoadNotes(int C_Order_ID)
        {
            List<NoteData> notes = new List<NoteData>();
            try
            {
                string sql = @"SELECT
                                  o.Description AS OrderNote,
                                  ol.Line       AS LineNo,
                                  ol.Description AS LineDescription,
                                  p.Name        AS ProductName,
                                  ch.Name       AS ChargeName
                                FROM C_Order o
                                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID)
                                LEFT OUTER JOIN M_Product p  ON (p.M_Product_ID = ol.M_Product_ID)
                                LEFT OUTER JOIN C_Charge  ch ON (ch.C_Charge_ID  = ol.C_Charge_ID)
                                WHERE o.C_Order_ID = @C_Order_ID
                                  AND o.IsActive   = 'Y'
                                  AND ol.IsActive  = 'Y'
                                ORDER BY ol.Line";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return notes;

                bool headerAdded = false;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    // Header note once (identical across lines).
                    if (!headerAdded)
                    {
                        string headerNote = Util.GetValueOfString(r["OrderNote"]);
                        if (!string.IsNullOrEmpty(headerNote))
                            notes.Add(new NoteData { NoteType = "header", Text = headerNote.Trim() });
                        headerAdded = true;
                    }

                    // Per-line note: only when the line carries its own description.
                    string lineDesc = Util.GetValueOfString(r["LineDescription"]);
                    if (string.IsNullOrEmpty(lineDesc)) continue;

                    string prod = Util.GetValueOfString(r["ProductName"]);
                    if (string.IsNullOrEmpty(prod)) prod = Util.GetValueOfString(r["ChargeName"]);

                    string text = string.IsNullOrEmpty(prod)
                        ? lineDesc.Trim()
                        : prod.Trim() + " — " + lineDesc.Trim();
                    notes.Add(new NoteData { NoteType = "line", Text = text });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNotes (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return notes;
        }

        /// <summary>
        /// Loads the active billing frequencies (C_Frequency) for the contract
        /// form's Billing Frequency selector.
        /// </summary>
        private List<FrequencyData> LoadFrequencies(Ctx ctx)
        {
            List<FrequencyData> list = new List<FrequencyData>();
            try
            {
                string sql = @"SELECT C_Frequency_ID, Name
                                 FROM C_Frequency
                                WHERE IsActive = 'Y'
                                  AND AD_Client_ID IN (0, @client)
                                ORDER BY Name";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@client", ctx.GetAD_Client_ID())
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return list;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new FrequencyData
                    {
                        C_Frequency_ID = Util.GetValueOfInt(r["C_Frequency_ID"]),
                        Name           = Util.GetValueOfString(r["Name"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadFrequencies: " + ex.Message);
            }
            return list;
        }

        // ================================================================= //
        //  WRITE SIDE — Complete Sales Order                                //
        // ================================================================= //

        /// <summary>
        /// Completes the sales order via the platform document engine
        /// (MOrder.ProcessIt("CO")). Refuses to run when the order is already
        /// completed / closed / voided / reversed, so completion is one-way and
        /// idempotent. Runs in its own transaction.
        /// </summary>
        public ActionResultData CompleteSalesOrder(Ctx ctx, int C_Order_ID)
        {
            ActionResultData res = new ActionResultData();
            if (C_Order_ID <= 0) { res.Message = "Invalid order"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS106Complete"));
            try
            {
                MOrder order = new MOrder(ctx, C_Order_ID, trx);
                if (order.Get_ID() != C_Order_ID || !order.IsSOTrx())
                {
                    res.Message = "Sales order not found";
                    return res;
                }

                string status = order.GetDocStatus();
                if (status == "CO" || status == "CL" || status == "VO" || status == "RE")
                {
                    res.Message = "Order is already " + status + " — cannot complete again";
                    return res;
                }

                bool processed;
                try
                {
                    order.SetDocAction(MOrder.DOCACTION_Complete);
                    processed = order.ProcessIt(MOrder.DOCACTION_Complete);
                }
                catch (Exception pex)
                {
                    processed = false;
                    res.Message = pex.Message;
                }

                if (!processed)
                {
                    if (string.IsNullOrEmpty(res.Message))
                        res.Message = order.GetProcessMsg();
                    trx.Rollback();
                    return res;
                }

                order.Save(trx);
                trx.Commit();

                res.Success    = true;
                res.DocStatus  = order.GetDocStatus();
                res.DocumentNo = order.GetDocumentNo();
                res.Message    = order.GetProcessMsg();
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { }
                res.Message = ex.Message;
                _log.Severe("CompleteSalesOrder (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            finally
            {
                try { trx.Close(); } catch { }
            }
            return res;
        }

        // ================================================================= //
        //  WRITE SIDE — Create Contract from an order line                  //
        // ================================================================= //

        /// <summary>
        /// Creates a single C_Contract from a service / charge order line,
        /// mirroring the platform's OLineCreateSalesContract logic (records the
        /// contract schedule on the order line, then builds the draft contract
        /// and links it back via C_OrderLine.C_Contract_ID). Guards: order
        /// completed, line belongs to order, line is service / charge, no
        /// existing contract, required fields present. One contract per line.
        /// </summary>
        public ActionResultData CreateContract(Ctx ctx, int C_Order_ID, int C_OrderLine_ID,
            int C_Frequency_ID, int noOfCycle, decimal qtyPerCycle,
            DateTime? startDate, DateTime? endDate)
        {
            ActionResultData res = new ActionResultData();
            if (C_Order_ID <= 0 || C_OrderLine_ID <= 0) { res.Message = "Invalid line"; return res; }
            if (C_Frequency_ID <= 0)   { res.Message = "Billing Frequency is required"; return res; }
            if (!startDate.HasValue)   { res.Message = "Start Date is required"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS106Contract"));
            try
            {
                MOrder order = new MOrder(ctx, C_Order_ID, trx);
                if (order.Get_ID() != C_Order_ID)
                {
                    res.Message = "Sales order not found"; return res;
                }
                // Contracts are only reachable once the order is completed.
                string status = order.GetDocStatus();
                if (status != "CO" && status != "CL")
                {
                    res.Message = "Complete the sales order before creating contracts"; return res;
                }

                MOrderLine line = new MOrderLine(ctx, C_OrderLine_ID, trx);
                if (line.Get_ID() != C_OrderLine_ID || line.GetC_Order_ID() != C_Order_ID)
                {
                    res.Message = "Order line does not belong to this order"; return res;
                }
                // Product (stockable) lines never carry a contract.
                if (line.GetC_Charge_ID() == 0 && line.GetM_Product_ID() > 0)
                {
                    string prodType = Util.GetValueOfString(DB.ExecuteScalar(
                        "SELECT ProductType FROM M_Product WHERE M_Product_ID = " + line.GetM_Product_ID(),
                        null, null));
                    if (prodType == "I")
                    {
                        res.Message = "Contracts apply to service / charge lines only"; return res;
                    }
                }
                if (line.GetC_Contract_ID() > 0)
                {
                    res.Message = "A contract already exists for this line"; return res;
                }

                if (qtyPerCycle <= 0) qtyPerCycle = line.GetQtyOrdered();
                if (qtyPerCycle <= 0) qtyPerCycle = 1;

                // Record the contract schedule on the line (matches the platform
                // process, which reads these back), then flag it as a contract.
                line.SetC_Frequency_ID(C_Frequency_ID);
                line.SetStartDate(startDate.Value);
                if (endDate.HasValue) line.SetEndDate(endDate.Value);
                line.SetQtyPerCycle(qtyPerCycle);
                if (noOfCycle > 0) line.SetNoofCycle(noOfCycle);
                line.SetIsContract(true);
                if (!line.Save(trx))
                {
                    res.Message = "Could not update the order line"; trx.Rollback(); return res;
                }

                // ---- Build the draft contract (X_C_Contract) ----
                X_C_Contract contract = new X_C_Contract(ctx, 0, trx);
                contract.SetAD_Client_ID(order.GetAD_Client_ID());
                contract.SetAD_Org_ID(order.GetAD_Org_ID());
                contract.SetDescription(order.GetDescription());
                contract.SetC_Order_ID(order.GetC_Order_ID());
                contract.SetC_OrderLine_ID(line.GetC_OrderLine_ID());
                contract.SetStartDate(startDate.Value);
                if (endDate.HasValue) contract.SetEndDate(endDate.Value);

                contract.SetC_BPartner_ID(order.GetC_BPartner_ID());
                contract.SetBill_Location_ID(order.GetBill_Location_ID());
                contract.SetBill_User_ID(order.GetBill_User_ID());
                contract.SetSalesRep_ID(order.GetSalesRep_ID());

                contract.SetC_Currency_ID(order.GetC_Currency_ID());
                contract.SetC_ConversionType_ID(order.GetC_ConversionType_ID());
                contract.SetC_PaymentTerm_ID(order.GetC_PaymentTerm_ID());
                contract.SetM_PriceList_ID(order.GetM_PriceList_ID());
                contract.SetC_Frequency_ID(C_Frequency_ID);
                contract.SetC_Project_ID(order.GetC_Project_ID());

                if (line.GetM_Product_ID() > 0) contract.SetM_Product_ID(line.GetM_Product_ID());
                if (line.GetC_UOM_ID() > 0)     contract.SetC_UOM_ID(line.GetC_UOM_ID());
                contract.SetC_Tax_ID(line.GetC_Tax_ID());

                // Prices come straight off the line (works for service + charge).
                decimal price = line.GetPriceActual();
                contract.SetPriceList(price);
                contract.SetPriceActual(price);
                contract.SetPriceEntered(price);
                contract.SetQtyEntered(qtyPerCycle);
                contract.SetDiscount(line.GetDiscount());

                decimal lineNet = decimal.Multiply(qtyPerCycle, price);
                decimal taxAmt = ComputeTaxAmt(line.GetC_Tax_ID(), price, qtyPerCycle);
                contract.SetLineNetAmt(lineNet);
                contract.SetTaxAmt(taxAmt);
                contract.SetGrandTotal(decimal.Add(lineNet, taxAmt));

                // Cycle count: honour the form; else derive from dates + frequency.
                int cycles = noOfCycle;
                if (cycles <= 0)
                    cycles = ComputeCycleCount(C_Frequency_ID, startDate.Value, endDate);
                contract.SetTotalInvoice(cycles);

                contract.SetDocStatus("DR");
                contract.SetRenewContract("N");

                if (!contract.Save(trx))
                {
                    res.Message = "Could not create the contract"; trx.Rollback(); return res;
                }

                // Link the contract back onto the line (enforces one per line).
                line.SetC_Contract_ID(contract.GetC_Contract_ID());
                line.Save(trx);

                trx.Commit();
                res.Success      = true;
                res.C_Contract_ID = contract.GetC_Contract_ID();
                res.DocumentNo    = contract.GetDocumentNo();
                res.Message       = "Contract created";
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { }
                res.Message = ex.Message;
                _log.Severe("CreateContract (C_OrderLine_ID=" + C_OrderLine_ID + "): " + ex.Message);
            }
            finally
            {
                try { trx.Close(); } catch { }
            }
            return res;
        }

        /// <summary>Tax amount = round(price * rate/100, 2) * qty.</summary>
        private decimal ComputeTaxAmt(int C_Tax_ID, decimal price, decimal qty)
        {
            if (C_Tax_ID <= 0) return 0;
            try
            {
                object o = DB.ExecuteScalar(
                    "SELECT Rate FROM C_Tax WHERE C_Tax_ID = " + C_Tax_ID, null, null);
                decimal rate = Util.GetValueOfDecimal(o);
                if (rate <= 0) return 0;
                decimal per = decimal.Round(decimal.Multiply(price, decimal.Divide(rate, 100)),
                                            2, MidpointRounding.AwayFromZero);
                return decimal.Multiply(per, qty);
            }
            catch { return 0; }
        }

        /// <summary>
        /// Cycle count from start/end dates against the frequency's NoOfDays
        /// (mirrors OLineCreateSalesContract). Falls back to 1.
        /// </summary>
        private int ComputeCycleCount(int C_Frequency_ID, DateTime start, DateTime? end)
        {
            if (!end.HasValue) return 1;
            try
            {
                object o = DB.ExecuteScalar(
                    "SELECT NoOfDays FROM C_Frequency WHERE C_Frequency_ID = " + C_Frequency_ID, null, null);
                int days = Util.GetValueOfInt(o);
                int total = (end.Value - start).Days;
                if (days > 0 && total > 0) return total / days;
            }
            catch { }
            return 1;
        }

        // ================================================================= //
        //  Helpers                                                          //
        // ================================================================= //

        private SqlParameter[] OrderParam(int C_Order_ID)
        {
            return new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) };
        }

        private string BuildAddress(string address1, string address2, string city,
                                    string region, string postal, string country)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(address1)) parts.Add(address1.Trim());
            if (!string.IsNullOrEmpty(address2)) parts.Add(address2.Trim());

            List<string> cityLine = new List<string>();
            if (!string.IsNullOrEmpty(city))   cityLine.Add(city.Trim());
            if (!string.IsNullOrEmpty(region)) cityLine.Add(region.Trim());
            if (!string.IsNullOrEmpty(postal)) cityLine.Add(postal.Trim());
            if (cityLine.Count > 0) parts.Add(string.Join(" ", cityLine));

            if (!string.IsNullOrEmpty(country)) parts.Add(country.Trim());
            return string.Join(", ", parts);
        }

        // ================================================================= //
        //  Data carriers                                                    //
        // ================================================================= //

        public class SalesOrderLineData
        {
            public int     C_OrderLine_ID { get; set; }
            public int     Line           { get; set; }
            public decimal QtyOrdered     { get; set; }
            public decimal QtyDelivered   { get; set; }
            public decimal QtyInvoiced    { get; set; }
            public decimal PriceActual    { get; set; }
            public decimal Discount       { get; set; }
            public decimal LineNetAmt     { get; set; }
            public string  Description    { get; set; }
            public int     M_Product_ID   { get; set; }
            public int     C_Charge_ID    { get; set; }
            public bool    IsContractFlag { get; set; }   // C_OrderLine.IsContract
            public int     C_Contract_ID  { get; set; }
            public string  ContractNo     { get; set; }
            public string  ProductValue   { get; set; }   // SKU
            public string  ProductName    { get; set; }
            public string  ProductType    { get; set; }   // I / S / ...
            public string  ChargeName     { get; set; }
            public string  UOMSymbol      { get; set; }
            public int     UOMPrecision   { get; set; }
            public string  LineType       { get; set; }   // product | service | charge | other
            public string  DeliveredState { get; set; }   // full | part | none
        }

        public class DeliveryReadinessData
        {
            public int     C_OrderLine_ID { get; set; }
            public string  ProductValue   { get; set; }
            public string  ProductName    { get; set; }
            public string  WarehouseName  { get; set; }
            public decimal PendingQty     { get; set; }
            public decimal QtyOnHand      { get; set; }
            public string  Readiness      { get; set; }   // ready | instock | short | awaited
        }

        public class DeliveryData
        {
            public int      M_InOut_ID    { get; set; }
            public string   DocumentNo    { get; set; }
            public string   DocStatus     { get; set; }
            public DateTime? MovementDate { get; set; }
            public string   TrackingNo    { get; set; }
            public string   WarehouseName { get; set; }
            public decimal  DeliveredQty  { get; set; }
            public int      LineCount     { get; set; }
        }

        public class InvoiceData
        {
            public int      C_Invoice_ID { get; set; }
            public string   DocumentNo   { get; set; }
            public string   DocStatus    { get; set; }
            public DateTime? DateInvoiced { get; set; }
            public decimal  GrandTotal   { get; set; }
            public bool     IsPaid       { get; set; }
        }

        public class ActivityData
        {
            public string   EventType { get; set; }   // Note | Delivery | Invoice | Created | Updated
            public string   Title     { get; set; }
            public string   ActorName { get; set; }
            public decimal  Amount    { get; set; }
            public DateTime? EventTime { get; set; }
        }

        public class NoteData
        {
            public string NoteType { get; set; }   // header | line
            public string Text     { get; set; }
        }

        public class FrequencyData
        {
            public int    C_Frequency_ID { get; set; }
            public string Name           { get; set; }
        }

        public class ActionResultData
        {
            public bool   Success       { get; set; }
            public string Message       { get; set; }
            public string DocStatus     { get; set; }
            public string DocumentNo    { get; set; }
            public int    C_Contract_ID { get; set; }
        }

        public class SalesOrderOverviewData
        {
            // Header / identity
            public int       C_Order_ID   { get; set; }
            public string    DocumentNo   { get; set; }
            public string    POReference  { get; set; }
            public DateTime? DateOrdered  { get; set; }
            public DateTime? DatePromised { get; set; }
            public string    DocStatus    { get; set; }
            public string    Posted       { get; set; }
            public string    PriorityRule { get; set; }
            public DateTime? Created      { get; set; }

            // Totals
            public decimal   GrandTotal   { get; set; }
            public decimal   TotalLines   { get; set; }
            public decimal   TaxAmt       { get; set; }

            // Currency
            public int       C_Currency_ID { get; set; }
            public string    CurSymbol     { get; set; }
            public string    ISO_Code      { get; set; }
            public int       StdPrecision  { get; set; }

            // Customer / contact
            public int       C_BPartner_ID  { get; set; }
            public string    CustomerName   { get; set; }
            public string    CreditStatus   { get; set; }   // O/H/S/W/X
            public string    ContactName    { get; set; }
            public string    ContactPhone   { get; set; }
            public string    ContactEmail   { get; set; }
            public string    SalesRepName   { get; set; }
            public string    PaymentTermName { get; set; }
            public string    PriceListName  { get; set; }
            public string    WarehouseName  { get; set; }
            public string    DeliveryRule   { get; set; }   // shipping rule A/F/L/M/O
            public string    InvoiceRule    { get; set; }
            public string    BillToAddress  { get; set; }
            public string    ShipToAddress  { get; set; }

            // Created from
            public int       QuotationId    { get; set; }
            public string    QuotationNo    { get; set; }
            public int       OpportunityId  { get; set; }
            public string    OpportunityName { get; set; }
            public int       ProjectId      { get; set; }
            public string    ProjectName    { get; set; }

            // Collections
            public List<SalesOrderLineData>   Lines            { get; set; }
            public List<DeliveryReadinessData> DeliveryReadiness { get; set; }
            public List<DeliveryData>         Deliveries       { get; set; }
            public List<InvoiceData>          Invoices         { get; set; }
            public List<ActivityData>         Activity         { get; set; }
            public List<NoteData>             Notes            { get; set; }
            public List<FrequencyData>        Frequencies      { get; set; }
        }
    }
}
