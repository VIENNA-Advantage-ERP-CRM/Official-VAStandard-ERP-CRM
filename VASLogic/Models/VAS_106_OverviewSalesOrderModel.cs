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
///   VAI163   2026-08-12  Added DeliveryRuleName: the DICTIONARY's own name for
///                        the shipping rule stored on the order, resolved through
///                        the COLUMN's reference list (AD_Column
///                        .AD_Reference_Value_ID -> AD_Ref_List, translated where
///                        the language has a translation) by the new
///                        LoadRefListName helper. The panel carried its own map of
///                        the values and was missing 'R' (After Receipt), so an
///                        order on that rule showed a bare "R"; reading the
///                        dictionary means the panel shows what the order screen
///                        shows, customer-added values and translations included.
///                        Its own map stays as the fallback.
///   VAI163   2026-08-12  - Lines and delivery-readiness rows carry their UOM name
///                          and their Attribute Set Instance. The ASI is joined
///                          only for a REAL instance (id > 0), so a line with no
///                          attributes cannot pick up the zero-record's "--"
///                          description.
///                        - Added Documents (LoadDocuments +
///                          LoadReceiptDocuments): the shipments, invoices and
///                          customer receipts raised against the order, in one
///                          flat list carrying TableName + RecordId, for the
///                          panel's single Documents section. Shipments and
///                          invoices are reused from the collections already
///                          loaded — so the section can never disagree with the
///                          KPIs derived from them — and the receipts are reached
///                          through C_AllocationLine -> C_Invoice, the sales-side
///                          mirror of the Purchase Order overview's payment rows.
///                          A shipment reports a null Amount: it has no monetary
///                          total, which is not the same as zero.
///   VAI163   2026-08-12  - Added CompletedDate (GetOrderCompletedDate: the
///                          workflow DocComplete stamp, falling back to the
///                          record's last change for an order completed outside
///                          the engine), which dates the progress line's Completed
///                          stage. It was dated by DateOrdered, a document field a
///                          user can back-date.
///                        - A chat note's author resolves from
///                          CM_ChatEntry.AD_User_ID falling back to CreatedBy: a
///                          note logged by the platform leaves AD_User_ID null, and
///                          those appeared in the feed with no name. Follows
///                          VAS_092.
///                        - Frequencies are no longer loaded on every panel read:
///                          they populated the inline contract form, which the
///                          panel no longer has. LoadFrequencies stays for the
///                          CreateContract endpoint.
///   VAI163   2026-08-12  - Order lines and delivery-readiness rows report their
///                          quantities and unit price in the line's SELECTED unit
///                          (QtyEntered / PriceEntered, with everything held in the
///                          base unit — QtyDelivered, QtyInvoiced, M_Storage's
///                          on-hand — scaled by the line's own base-per-selected
///                          ratio). The panel labels each row with that unit while
///                          being handed the BASE figures, so a line sold as 2 BOX
///                          of an EA-held product reported 24 at the per-EA price.
///                        - The readiness state is ready / instock / short; the
///                          "awaited" state is gone, matching the panel, which no
///                          longer has a tag for it.
///                        - The activity cap rose from 15 to 200. It was the same
///                          number the panel PAGES at, so the feed could never
///                          exceed one page and the sixteenth event onwards was
///                          unreachable rather than merely further down.
///                        - Added GetWindowId (ported from VAS_092), so the panel
///                          can name the SALES-side window of a dual-purpose table
///                          instead of letting the browser resolve the purchase
///                          one — an AR invoice was opening the AP Invoice screen.
///   VAI163   2026-08-12  - Added LoadHistory (C_OrderLineHistory): the prior
///                          versions of each order line, for the panel's per-line
///                          history drawer. Filtered on the history row's own
///                          C_Order_ID with a LEFT JOIN to the current line, so a
///                          line later removed still reports its snapshots. Ported
///                          from VAS_092, with COALESCE for NVL so it runs on both
///                          databases, and the optional columns dictionary-guarded.
///                        - Added LoadBlanketOrigin (C_Order.C_Order_Blanket): the
///                          blanket sales order this one was released against. Read
///                          in its own guarded statement — C_Order_Blanket is a
///                          module column, and putting it in the created-from query
///                          would have cost that query its other origins on a schema
///                          without it. An order released from a blanket showed no
///                          origin at all and the panel called it "Manual".
///                        - Added ColumnExists, which both of the above need.
///   VAI163   2026-08-12  - LoadCreatedFrom splits into one guarded statement per
///                          origin (quotation / opportunity / project / contract /
///                          blanket). They shared a single SELECT, so a schema
///                          missing any ONE of those optional module columns lost
///                          them ALL — the statement failed, the catch swallowed
///                          it, and an order raised from a project or a quotation
///                          reported no origin at all, which the panel then called
///                          "Manual". One absent column now costs only its own chip.
///                        - Added LoadContractOrigin: the contract master on the
///                          header (C_Order.VAS_ContractMaster_ID, the same link
///                          the PO overview's Contract chip uses — C_Order serves
///                          both sides) falling back to the service contract behind
///                          one of the lines (C_OrderLine.C_Contract_ID).
///                          ContractTable travels with it so the panel opens the
///                          right record.
///                        - The project origin carries its NUMBER (C_Project.Value)
///                          as well as its name: the strip names documents by their
///                          identifier.
///   VAI163   2026-08-12  Added GetWindowIdByTable (ported from VAS_102): the
///                        window a TABLE opens in, read from the dictionary, for a
///                        record whose screen cannot be named on the client. The
///                        Contract chip needs it — C_Contract and
///                        VAS_ContractMaster are maintained by module windows
///                        whose names cannot be hard-coded, so nothing else could
///                        resolve them.
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

            // The dictionary's own name for the shipping rule stored on the order,
            // so the panel reads what the order screen reads.
            result.DeliveryRuleName = LoadRefListName(ctx, "C_Order", "DeliveryRule", result.DeliveryRule);

            // When the order was actually completed, for the progress line's
            // Completed stage.
            result.CompletedDate = GetOrderCompletedDate(C_Order_ID);

            // ----- Child data -----
            LoadAddresses(shipLocId, billLocId, result);
            LoadCreatedFrom(C_Order_ID, result);
            result.Lines            = LoadLines(C_Order_ID);
            result.History          = LoadHistory(C_Order_ID);
            result.DeliveryReadiness = LoadDeliveryReadiness(C_Order_ID);
            result.Deliveries       = LoadDeliveries(C_Order_ID);
            result.Invoices         = LoadInvoices(C_Order_ID);
            // One flat list for the Documents section, built from the two above
            // plus the receipts allocated to those invoices.
            result.Documents        = LoadDocuments(C_Order_ID, result.Deliveries, result.Invoices);
            result.Activity         = LoadActivity(C_Order_ID);
            result.Notes            = LoadNotes(C_Order_ID);
            // Frequencies are no longer loaded: they populated the panel's inline
            // contract form, which is gone — the Contract cell is read-only now.
            // LoadFrequencies stays for the CreateContract endpoint's sake, so the
            // list is a query away if the flow ever returns; running it on every
            // panel load fed nothing.

            return result;
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's record-open path.
        ///
        /// The documents this panel opens are all SALES-side records of tables that
        /// serve both sides — C_Invoice is an AR invoice here, C_Payment an AR
        /// receipt, M_InOut a shipment — and the browser's zoom lookup resolved the
        /// purchase-side window for them, so an invoice number opened the AP Invoice
        /// screen. Naming the window is what settles it.
        ///
        /// Restricted to windows this tenant can see (AD_Client_ID 0 or its own),
        /// preferring the tenant's own row over the system one. Whether the ROLE may
        /// open it is the platform's call, made when the window is started. Ported
        /// from VAS_092.
        /// </summary>
        /// <param name="ctx">User context (client).</param>
        /// <param name="windowName">Window name to resolve.</param>
        /// <returns>The window id, or 0 when the name resolves to nothing.</returns>
        public int GetWindowId(Ctx ctx, string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return 0;
            try
            {
                string sql = @"SELECT w.AD_Window_ID
                                 FROM AD_Window w
                                WHERE w.Name         = @Name
                                  AND w.IsActive     = 'Y'
                                  AND w.AD_Client_ID IN (0, @AD_Client_ID)
                                ORDER BY w.AD_Client_ID DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@Name", windowName.Trim()),
                    new SqlParameter("@AD_Client_ID", ctx == null ? 0 : ctx.GetAD_Client_ID())
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowId (" + windowName + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Resolves the window a TABLE's records open in: the table's own zoom
        /// target (AD_Table.AD_Window_ID), falling back to the first window that
        /// has a tab on the table.
        ///
        /// This is the panel's last resort for a record whose screen cannot be
        /// named on the client. A contract is the case that needed it: C_Contract
        /// and VAS_ContractMaster are maintained by module windows whose names
        /// cannot be hard-coded here, and the browser-side zoom lookup only knows
        /// tables the client has cached — so the Contract chip fell through to the
        /// "cannot open" toast. Reading the dictionary works for any installed
        /// module, and any future chip gets the same safety net. Ported from
        /// VAS_102.
        ///
        /// Each statement carries a single bind name, occurring once: positional
        /// binding gives a repeated name a second, unfilled placeholder.
        /// </summary>
        /// <param name="ctx">User context (unused today; kept for symmetry with
        /// <see cref="GetWindowId"/>, which filters by client).</param>
        /// <param name="tableName">Physical table name, e.g. "C_Contract".</param>
        /// <returns>The window id, or 0 when the table has no window at all.</returns>
        public int GetWindowIdByTable(Ctx ctx, string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) return 0;
            string name = tableName.Trim();
            try
            {
                string sql = @"SELECT t.AD_Window_ID
                                 FROM AD_Table t
                                WHERE UPPER(t.TableName) = UPPER(@TableName)
                                  AND t.IsActive         = 'Y'
                                  AND COALESCE(t.AD_Window_ID, 0) > 0";
                DataSet ds = DB.ExecuteDataset(
                    sql, new SqlParameter[] { new SqlParameter("@TableName", name) }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int id = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
                    if (id > 0) return id;
                }

                // No zoom target on the table itself — take the window whose first
                // tab sits on this table, which is the screen that maintains it.
                sql = @"SELECT tb.AD_Window_ID
                          FROM AD_Tab tb
                         INNER JOIN AD_Table t ON (t.AD_Table_ID = tb.AD_Table_ID)
                         WHERE UPPER(t.TableName) = UPPER(@TableName)
                           AND tb.IsActive        = 'Y'
                           AND t.IsActive         = 'Y'
                         ORDER BY tb.SeqNo, tb.AD_Tab_ID";
                ds = DB.ExecuteDataset(
                    sql, new SqlParameter[] { new SqlParameter("@TableName", name) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowIdByTable (" + name + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// The moment the order was completed: the Created stamp of its workflow
        /// DocComplete activity, falling back to the record's own last change for
        /// an order that reached CO / CL outside the workflow engine.
        ///
        /// A standalone query, never a subselect of the MRole-rewritten header
        /// SELECT — the role filter rewrites that statement and an added subselect
        /// can fail against it.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <returns>The completion stamp, or null when the order has none.</returns>
        private DateTime? GetOrderCompletedDate(int C_Order_ID)
        {
            try
            {
                string sql = @"SELECT MAX(wfa.Created) AS CompletedDate
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                WHERE wfp.Record_ID  = @C_Order_ID
                                  AND adt.TableName  = 'C_Order'
                                  AND wfp.IsActive   = 'Y'
                                  AND wfa.IsActive   = 'Y'
                                  AND wfa.WFState    = 'CC'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DateTime? d = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["CompletedDate"]);
                    if (d.HasValue) return d;
                }

                // Completed outside the workflow engine — the last change to the
                // record is the closest stamp there is.
                string fallback = @"SELECT o.Updated
                                      FROM C_Order o
                                     WHERE o.C_Order_ID = @C_Order_ID
                                       AND o.DocStatus IN ('CO', 'CL')";
                ds = DB.ExecuteDataset(fallback, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["Updated"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetOrderCompletedDate (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The dictionary's display name for one value of a reference-list column
        /// — the label the record screen shows for it.
        ///
        /// The reference is reached through the COLUMN
        /// (AD_Column.AD_Reference_Value_ID), not by hard-coding a reference id, so
        /// a deployment that points the column at its own list is read correctly.
        /// Translated where the user's language has a translation.
        ///
        /// Returns "" when the value, the column or the list entry cannot be
        /// resolved, which leaves the panel on its own built-in labels.
        /// </summary>
        /// <param name="ctx">User context, for the language.</param>
        /// <param name="tableName">Table owning the column, e.g. "C_Order".</param>
        /// <param name="columnName">Reference-list column, e.g. "DeliveryRule".</param>
        /// <param name="value">The stored value, e.g. "R".</param>
        /// <returns>The list entry's name, or "".</returns>
        private string LoadRefListName(Ctx ctx, string tableName, string columnName, string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                string lang = (ctx == null) ? "" : ctx.GetAD_Language();

                // The translated name where one exists, else the base name. Each
                // bind name occurs exactly once: positional binding gives a
                // repeated name a second, unfilled placeholder.
                string sql = @"SELECT COALESCE(rlt.Name, rl.Name) AS Name
                                 FROM AD_Ref_List rl
                                INNER JOIN AD_Column c
                                        ON (c.AD_Reference_Value_ID = rl.AD_Reference_ID)
                                INNER JOIN AD_Table t
                                        ON (t.AD_Table_ID = c.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Ref_List_Trl rlt
                                        ON (rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID
                                            AND rlt.AD_Language = @AD_Language)
                                WHERE UPPER(t.TableName)  = UPPER(@TableName)
                                  AND UPPER(c.ColumnName) = UPPER(@ColumnName)
                                  AND rl.Value            = @Value
                                  AND rl.IsActive         = 'Y'";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@AD_Language", lang),
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@Value", value)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return "";
                return Util.GetValueOfString(ds.Tables[0].Rows[0]["Name"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadRefListName (" + tableName + "." + columnName + "=" + value + "): " + ex.Message);
                return "";
            }
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
            // Each origin is read under its OWN guard and its own statement.
            //
            // They used to share one SELECT, which meant a schema missing any ONE
            // of the module columns lost them ALL: the statement failed, the catch
            // swallowed it, and an order raised from a project or a quotation
            // reported no origin at all — the strip then called it "Manual". The
            // columns are optional precisely because not every deployment carries
            // them, so one absent column must only ever cost its own chip.
            LoadQuotationOrigin(C_Order_ID, d);
            LoadOpportunityOrigin(C_Order_ID, d);
            LoadProjectOrigin(C_Order_ID, d);
            LoadContractOrigin(C_Order_ID, d);
            LoadBlanketOrigin(C_Order_ID, d);
        }

        /// <summary>The quotation this order was raised from
        /// (C_Order.C_Order_Quotation).</summary>
        private void LoadQuotationOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (!ColumnExists("C_Order", "C_Order_Quotation")) return;
            try
            {
                string sql = @"SELECT q.C_Order_ID AS QuotationId, q.DocumentNo AS QuotationNo
                                 FROM C_Order o
                                INNER JOIN C_Order q
                                        ON (q.C_Order_ID = o.C_Order_Quotation AND q.IsActive = 'Y')
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.QuotationId = Util.GetValueOfInt(r["QuotationId"]);
                d.QuotationNo = Util.GetValueOfString(r["QuotationNo"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadQuotationOrigin (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>The opportunity this order was won from
        /// (C_Order.VAS_Opportunity_ID).</summary>
        private void LoadOpportunityOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (!ColumnExists("C_Order", "VAS_Opportunity_ID")) return;
            try
            {
                string sql = @"SELECT opp.VAS_Opportunity_ID AS OpportunityId, opp.Name AS OpportunityName
                                 FROM C_Order o
                                INNER JOIN VAS_Opportunity opp
                                        ON (opp.VAS_Opportunity_ID = o.VAS_Opportunity_ID
                                            AND opp.IsActive = 'Y')
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.OpportunityId   = Util.GetValueOfInt(r["OpportunityId"]);
                d.OpportunityName = Util.GetValueOfString(r["OpportunityName"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadOpportunityOrigin (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The project this order was raised for (C_Order.C_Project_ID).
        ///
        /// The project's NUMBER (C_Project.Value) travels with its name: the strip
        /// names documents by their identifier, and the name is what the chip's
        /// tooltip carries.
        /// </summary>
        private void LoadProjectOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (!ColumnExists("C_Order", "C_Project_ID")) return;
            try
            {
                string sql = @"SELECT p.C_Project_ID AS ProjectId,
                                      p.Value        AS ProjectNo,
                                      p.Name         AS ProjectName
                                 FROM C_Order o
                                INNER JOIN C_Project p
                                        ON (p.C_Project_ID = o.C_Project_ID AND p.IsActive = 'Y')
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.ProjectId   = Util.GetValueOfInt(r["ProjectId"]);
                d.ProjectNo   = Util.GetValueOfString(r["ProjectNo"]);
                d.ProjectName = Util.GetValueOfString(r["ProjectName"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadProjectOrigin (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The contract this order references.
        ///
        /// Two links are possible and both are read, header first:
        ///   * C_Order.VAS_ContractMaster_ID — the contract master the order was
        ///     raised against, the same link the Purchase Order overview's Contract
        ///     chip uses (C_Order serves both sides, so the column is shared);
        ///   * C_OrderLine.C_Contract_ID — the service contract tied to one of the
        ///     order's lines, which is what the panel's own Contract column shows.
        ///
        /// Both are module columns, so each is dictionary-guarded and read in its
        /// own statement. An order referencing neither reports no contract and the
        /// strip falls through to its next origin.
        /// </summary>
        private void LoadContractOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            // ----- The contract master on the header -----
            if (ColumnExists("C_Order", "VAS_ContractMaster_ID") &&
                ColumnExists("VAS_ContractMaster", "DocumentNo"))
            {
                try
                {
                    string sql = @"SELECT cm.VAS_ContractMaster_ID AS ContractId,
                                          cm.DocumentNo            AS ContractNo
                                     FROM C_Order o
                                    INNER JOIN VAS_ContractMaster cm
                                            ON (cm.VAS_ContractMaster_ID = o.VAS_ContractMaster_ID)
                                    WHERE o.C_Order_ID = @C_Order_ID";
                    DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    {
                        DataRow r = ds.Tables[0].Rows[0];
                        d.ContractId    = Util.GetValueOfInt(r["ContractId"]);
                        d.ContractNo    = Util.GetValueOfString(r["ContractNo"]);
                        d.ContractTable = "VAS_ContractMaster";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _log.Severe("LoadContractOrigin/master (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                }
            }

            // ----- Else the service contract behind one of the lines -----
            if (!ColumnExists("C_OrderLine", "C_Contract_ID")) return;
            try
            {
                string sql = @"SELECT DISTINCT c.C_Contract_ID AS ContractId,
                                               c.DocumentNo    AS ContractNo
                                 FROM C_OrderLine ol
                                INNER JOIN C_Contract c
                                        ON (c.C_Contract_ID = ol.C_Contract_ID)
                                WHERE ol.C_Order_ID = @C_Order_ID
                                  AND ol.IsActive   = 'Y'
                                ORDER BY c.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.ContractId    = Util.GetValueOfInt(r["ContractId"]);
                d.ContractNo    = Util.GetValueOfString(r["ContractNo"]);
                d.ContractTable = "C_Contract";
                // Several lines can carry different contracts; the first is named
                // and the rest counted on the chip.
                d.ContractCount = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadContractOrigin/line (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The blanket sales order this order was released against
        /// (C_Order.C_Order_Blanket -> the blanket, which is itself a C_Order).
        ///
        /// Read in its own statement under its own guard: C_Order_Blanket is a
        /// module column that not every schema carries, and putting it in the query
        /// above would have cost that query its quotation / opportunity / project
        /// origins on any schema without it. An order released from a blanket used
        /// to show no origin at all and the panel called it "Manual".
        ///
        /// IsBlanketTrx on the parent is deliberately NOT required: the release
        /// order's own C_Order_Blanket reference is the part of the link the
        /// platform always writes, and demanding the flag hides the chip wherever
        /// it is not carried. Follows VAS_092.
        /// </summary>
        private void LoadBlanketOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (!ColumnExists("C_Order", "C_Order_Blanket")) return;

            try
            {
                string sql = @"SELECT bo.C_Order_ID AS BlanketOrderId,
                                      bo.DocumentNo AS BlanketOrderNo
                                 FROM C_Order o
                                INNER JOIN C_Order bo
                                        ON (bo.C_Order_ID = o.C_Order_Blanket)
                                WHERE o.C_Order_ID = @C_Order_ID
                                  AND bo.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.BlanketOrderId = Util.GetValueOfInt(r["BlanketOrderId"]);
                d.BlanketOrderNo = Util.GetValueOfString(r["BlanketOrderNo"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadBlanketOrigin (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads the line change history (C_OrderLineHistory) for every line of the
        /// order — the prior versions the platform snapshots when a completed order
        /// line is re-activated and edited — newest change first per line.
        ///
        /// Filtered on the history row's OWN C_Order_ID with a LEFT JOIN to the
        /// current line, so a line that was later removed still reports its
        /// snapshots (falling back to the snapshot's own Line sequence).
        ///
        /// The panel draws a version beneath its line using the same columns as the
        /// line itself, so the snapshot's entered quantity and unit price are what
        /// it reads. Both are on the SELECTED unit where the snapshot carries it
        /// (QtyEntered / PriceEntered), matching the line above. Optional columns
        /// are dictionary-guarded: a schema whose history table omits one simply
        /// reports nothing for it. Ported from VAS_092, with COALESCE in place of
        /// NVL so the statement runs on both databases.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <returns>History rows, ordered by line then newest change; never null.</returns>
        private List<LineHistoryData> LoadHistory(int C_Order_ID)
        {
            List<LineHistoryData> history = new List<LineHistoryData>();
            try
            {
                string qtyEnteredExpr = ColumnExists("C_OrderLineHistory", "QtyEntered")
                    ? "COALESCE(olh.QtyEntered, olh.QtyOrdered)" : "olh.QtyOrdered";
                string priceEnteredExpr = ColumnExists("C_OrderLineHistory", "PriceEntered")
                    ? "COALESCE(olh.PriceEntered, olh.PriceActual)" : "olh.PriceActual";
                string discountExpr = ColumnExists("C_OrderLineHistory", "Discount")
                    ? "COALESCE(olh.Discount, 0)" : "0";

                string sql = @"SELECT olh.C_OrderLine_ID,
                                      COALESCE(ol.Line, olh.Line) AS LineNo,
                                      olh.Updated      AS ChangedOn,
                                      uu.Name          AS UpdatedByName,
                                      " + qtyEnteredExpr   + @" AS QtyEntered,
                                      " + priceEnteredExpr + @" AS PriceEntered,
                                      " + discountExpr     + @" AS Discount,
                                      COALESCE(olh.LineNetAmt, 0) AS LineNetAmt,
                                      olh.Description  AS LineDescription,
                                      uom.Name         AS UOMName,
                                      COALESCE(uom.StdPrecision, 0) AS UOMPrecision
                                 FROM C_OrderLineHistory olh
                                INNER JOIN C_Order o           ON (o.C_Order_ID = olh.C_Order_ID)
                                 LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = olh.C_OrderLine_ID)
                                 LEFT OUTER JOIN C_UOM uom      ON (uom.C_UOM_ID = olh.C_UOM_ID)
                                 LEFT OUTER JOIN AD_User uu     ON (uu.AD_User_ID = olh.UpdatedBy)
                                WHERE olh.C_Order_ID = @C_Order_ID
                                ORDER BY COALESCE(ol.Line, olh.Line), olh.Updated DESC";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return history;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    LineHistoryData h = new LineHistoryData();
                    h.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                    h.LineNo         = Util.GetValueOfInt(r["LineNo"]);
                    h.ChangedOn      = Util.GetValueOfDateTime(r["ChangedOn"]);
                    h.UpdatedByName  = Util.GetValueOfString(r["UpdatedByName"]);
                    h.QtyEntered     = Util.GetValueOfDecimal(r["QtyEntered"]);
                    h.PriceEntered   = Util.GetValueOfDecimal(r["PriceEntered"]);
                    h.Discount       = Util.GetValueOfDecimal(r["Discount"]);
                    h.LineNetAmt     = Util.GetValueOfDecimal(r["LineNetAmt"]);
                    h.Description    = Util.GetValueOfString(r["LineDescription"]);
                    h.UOMName        = Util.GetValueOfString(r["UOMName"]);
                    h.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);
                    history.Add(h);
                }
            }
            catch (Exception ex)
            {
                // A schema without C_OrderLineHistory reaches here; the panel then
                // shows no history button on any line.
                _log.Severe("LoadHistory (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return history;
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
                                  COALESCE(ol.QtyEntered, 0)   AS QtyEntered,
                                  COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                                  COALESCE(ol.QtyInvoiced, 0)  AS QtyInvoiced,
                                  COALESCE(ol.PriceActual, 0)  AS PriceActual,
                                  COALESCE(ol.PriceEntered, 0) AS PriceEntered,
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
                                  uom.Name       AS UOMName,
                                  COALESCE(uom.StdPrecision, 0) AS UOMPrecision,
                                  asi.Description AS AttributeSetInstance,
                                  con.DocumentNo AS ContractNo
                                FROM C_OrderLine ol
                                LEFT OUTER JOIN M_Product  p   ON (ol.M_Product_ID = p.M_Product_ID)
                                LEFT OUTER JOIN C_Charge   ch  ON (ol.C_Charge_ID  = ch.C_Charge_ID)
                                LEFT OUTER JOIN C_UOM      uom ON (ol.C_UOM_ID     = uom.C_UOM_ID)
                                -- Only a REAL instance is joined: id 0 is the
                                -- dictionary's no-attributes row, whose
                                -- description is a bare double dash that would
                                -- otherwise print against every line carrying no
                                -- attributes at all.
                                LEFT OUTER JOIN M_AttributeSetInstance asi
                                       ON (asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                                           AND ol.M_AttributeSetInstance_ID > 0)
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
                    // The row is reported in the line's SELECTED unit — the one it
                    // is labelled with (C_UOM_ID). C_OrderLine keeps two scales:
                    // QtyEntered / PriceEntered are in that unit, while QtyOrdered,
                    // QtyDelivered and PriceActual are in the product's BASE unit.
                    // The panel showed the base figures against the selected unit's
                    // name, so a line sold as 2 BOX of an EA-held product read as 24
                    // at the per-EA price.
                    //
                    // UomRatio is how many BASE units one SELECTED unit is (12, for
                    // a 12-EA box), derived from the line's own two quantities so it
                    // is exactly the ratio the line was saved with. Everything the
                    // database holds in the base unit is brought onto the selected
                    // scale with it.
                    decimal baseQty = Util.GetValueOfDecimal(r["QtyOrdered"]);
                    decimal entered = Util.GetValueOfDecimal(r["QtyEntered"]);
                    if (entered == 0) entered = baseQty;
                    ln.UomRatio       = (entered != 0 && baseQty != 0) ? baseQty / entered : 1;

                    ln.QtyOrdered     = entered;
                    ln.QtyDelivered   = Util.GetValueOfDecimal(r["QtyDelivered"]);
                    if (ln.UomRatio != 0) ln.QtyDelivered = ln.QtyDelivered / ln.UomRatio;
                    ln.QtyInvoiced    = Util.GetValueOfDecimal(r["QtyInvoiced"]);
                    if (ln.UomRatio != 0) ln.QtyInvoiced = ln.QtyInvoiced / ln.UomRatio;

                    // Price per SELECTED unit. PriceEntered is stored in it; a line
                    // saved without one falls back to the base price scaled by the
                    // ratio, which leaves the line's own total untouched:
                    // QtyEntered x (PriceActual x ratio) = QtyOrdered x PriceActual.
                    ln.PriceActual    = Util.GetValueOfDecimal(r["PriceEntered"]);
                    if (ln.PriceActual == 0)
                        ln.PriceActual = Util.GetValueOfDecimal(r["PriceActual"]) * ln.UomRatio;
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
                    ln.UOMName        = Util.GetValueOfString(r["UOMName"]);
                    ln.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);
                    ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);

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
                                  uom.Name AS UOMName,
                                  asi.Description AS AttributeSetInstance,
                                  wh.Name AS WarehouseName,
                                  COALESCE(ol.QtyOrdered, 0)   AS QtyOrdered,
                                  COALESCE(ol.QtyEntered, 0)   AS QtyEntered,
                                  COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                                  COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) AS PendingQty,
                                  COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) AS QtyOnHand
                                FROM C_Order o
                                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID)
                                INNER JOIN M_Product p     ON (p.M_Product_ID = ol.M_Product_ID)
                                LEFT OUTER JOIN C_UOM uom  ON (uom.C_UOM_ID = ol.C_UOM_ID)
                                -- A real instance only; see LoadLines.
                                LEFT OUTER JOIN M_AttributeSetInstance asi
                                       ON (asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                                           AND ol.M_AttributeSetInstance_ID > 0)
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
                                GROUP BY ol.C_OrderLine_ID, p.M_Product_ID, p.Value, p.Name,
                                         uom.Name, asi.Description, wh.Name,
                                         COALESCE(ol.QtyOrdered, 0), COALESCE(ol.QtyEntered, 0),
                                         COALESCE(ol.QtyDelivered, 0)
                                ORDER BY ol.C_OrderLine_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    DeliveryReadinessData rd = new DeliveryReadinessData();
                    rd.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                    rd.ProductValue   = Util.GetValueOfString(r["ProductValue"]);
                    rd.ProductName    = Util.GetValueOfString(r["ProductName"]);
                    rd.UOMName        = Util.GetValueOfString(r["UOMName"]);
                    rd.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
                    rd.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);

                    // Both quantities are converted onto the line's SELECTED unit —
                    // the unit the row is now labelled with — so pending and on-hand
                    // read against each other and against the order line above.
                    // M_Storage holds the product's base unit, and so do
                    // QtyOrdered / QtyDelivered.
                    decimal baseQty = Util.GetValueOfDecimal(r["QtyOrdered"]);
                    decimal entered = Util.GetValueOfDecimal(r["QtyEntered"]);
                    if (entered == 0) entered = baseQty;
                    decimal ratio = (entered != 0 && baseQty != 0) ? baseQty / entered : 1;

                    rd.PendingQty     = Util.GetValueOfDecimal(r["PendingQty"]);
                    rd.QtyOnHand      = Util.GetValueOfDecimal(r["QtyOnHand"]);
                    if (ratio != 0)
                    {
                        rd.PendingQty = rd.PendingQty / ratio;
                        rd.QtyOnHand  = rd.QtyOnHand  / ratio;
                    }

                    // Ready or short — the panel has no "awaited" state any more, so
                    // nothing on hand is simply the shortest of the short.
                    if (rd.PendingQty <= 0)                 rd.Readiness = "ready";     // fully delivered
                    else if (rd.QtyOnHand >= rd.PendingQty) rd.Readiness = "instock";   // can ship now
                    else                                    rd.Readiness = "short";     // not enough on hand
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
        /// Every document raised FROM this sales order, for the panel's Documents
        /// section: the shipments that delivered it, the invoices that billed it
        /// and the customer receipts that paid those invoices.
        ///
        /// Built like the Purchase Order overview's own Documents list — one flat
        /// collection, each row carrying the table + record id the panel opens it
        /// with — so the section navigates and reads the same way on both screens.
        /// It replaces the panel's separate Deliveries and Invoices sections; the
        /// Deliveries / Invoices collections themselves stay, because the KPI strip
        /// and the progress stepper are derived from them.
        ///
        /// Each source is loaded independently and swallows its own failure, so one
        /// DB-level problem drops only its own rows.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="deliveries">Already-loaded shipments, reused rather than re-queried.</param>
        /// <param name="invoices">Already-loaded invoices, reused rather than re-queried.</param>
        /// <returns>Documents, newest first; never null.</returns>
        private List<SalesOrderDocumentData> LoadDocuments(
            int C_Order_ID, List<DeliveryData> deliveries, List<InvoiceData> invoices)
        {
            List<SalesOrderDocumentData> docs = new List<SalesOrderDocumentData>();

            // Shipments and invoices are already in hand — the same rows the KPIs
            // are derived from, so the section can never disagree with them.
            if (deliveries != null)
            {
                foreach (DeliveryData dv in deliveries)
                {
                    docs.Add(new SalesOrderDocumentData
                    {
                        Type       = "delivery",
                        TableName  = "M_InOut",
                        RecordId   = dv.M_InOut_ID,
                        DocumentNo = dv.DocumentNo,
                        DocStatus  = dv.DocStatus,
                        DocDate    = dv.MovementDate,
                        // A shipment has no monetary total of its own; null, not a
                        // zero the reader would take for an amount.
                        Amount     = null,
                        LineCount  = dv.LineCount,
                        Extra      = dv.TrackingNo
                    });
                }
            }
            if (invoices != null)
            {
                foreach (InvoiceData iv in invoices)
                {
                    docs.Add(new SalesOrderDocumentData
                    {
                        Type       = "invoice",
                        TableName  = "C_Invoice",
                        RecordId   = iv.C_Invoice_ID,
                        DocumentNo = iv.DocumentNo,
                        DocStatus  = iv.DocStatus,
                        DocDate    = iv.DateInvoiced,
                        Amount     = iv.GrandTotal,
                        IsPaid     = iv.IsPaid
                    });
                }
            }

            LoadReceiptDocuments(C_Order_ID, docs);

            // Newest first; entries with no document date sink to the bottom.
            docs.Sort((a, b) =>
                b.DocDate.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.DocDate.GetValueOrDefault(DateTime.MinValue)));
            return docs;
        }

        /// <summary>
        /// Adds the customer receipts (C_Payment, IsReceipt = 'Y') allocated to
        /// this order's invoices, through C_AllocationLine -> C_Invoice. DISTINCT,
        /// so a receipt allocated across several of the order's invoices is listed
        /// once. Reversed / voided payments are excluded. The sales-side mirror of
        /// the Purchase Order overview's payment rows.
        /// </summary>
        private void LoadReceiptDocuments(int C_Order_ID, List<SalesOrderDocumentData> list)
        {
            try
            {
                string sql = @"SELECT DISTINCT p.C_Payment_ID,
                                               p.DocumentNo,
                                               p.DocStatus,
                                               p.DateTrx,
                                               COALESCE(p.PayAmt, 0)      AS PayAmt,
                                               COALESCE(p.DiscountAmt, 0) AS DiscountAmt
                                 FROM C_Payment p
                                INNER JOIN C_AllocationLine al ON (al.C_Payment_ID = p.C_Payment_ID)
                                INNER JOIN C_Invoice ci        ON (al.C_Invoice_ID = ci.C_Invoice_ID)
                                WHERE ci.C_Order_ID = @C_Order_ID
                                  AND p.IsActive    = 'Y'
                                  AND p.IsReceipt   = 'Y'
                                  AND p.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new SalesOrderDocumentData
                    {
                        Type        = "receipt",
                        TableName   = "C_Payment",
                        RecordId    = Util.GetValueOfInt(r["C_Payment_ID"]),
                        DocumentNo  = Util.GetValueOfString(r["DocumentNo"]),
                        DocStatus   = Util.GetValueOfString(r["DocStatus"]),
                        DocDate     = Util.GetValueOfDateTime(r["DateTrx"]),
                        Amount      = Util.GetValueOfDecimal(r["PayAmt"]),
                        DiscountAmt = Util.GetValueOfDecimal(r["DiscountAmt"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadReceiptDocuments (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
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
            // A runaway guard, not a headline count. It used to be 15 — the same
            // number the panel PAGES at — so the feed could never exceed one page,
            // the pager never appeared, and the sixteenth event onwards was
            // unreachable rather than merely further down. 200 matches the other
            // overview panels.
            const int MAX_ENTRIES = 200;
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
                // The author resolves from CM_ChatEntry.AD_User_ID falling back to
                // CreatedBy: a note logged by the platform itself leaves AD_User_ID
                // null, and those notes appeared in the feed with no name against
                // them. Follows VAS_092.
                string sql = @"SELECT ce.CharacterData, ce.Created, u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                 INNER JOIN CM_Chat ch     ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = COALESCE(ce.AD_User_ID, ce.CreatedBy))
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

        /// <summary>
        /// Returns true when the given column exists on the given table, using the
        /// AD_Column dictionary. A DB issue degrades to "absent" (false), which
        /// just drops the optional value that depends on it.
        /// </summary>
        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                string sql = @"SELECT COUNT(*) FROM AD_Column
                                WHERE UPPER(ColumnName) = UPPER(@ColumnName)
                                  AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table
                                                      WHERE UPPER(TableName) = UPPER(@TableName))";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@TableName", tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("ColumnExists (" + tableName + "." + columnName + "): " + ex.Message);
                return false;
            }
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
            // Quantities and the unit price are all in the line's SELECTED unit,
            // the one the row is labelled with. UomRatio carries the base scale the
            // database stores, for converting anything read from stock.
            public decimal QtyOrdered     { get; set; }
            public decimal QtyDelivered   { get; set; }
            public decimal QtyInvoiced    { get; set; }
            public decimal UomRatio       { get; set; }   // base units per selected unit
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
            public string  UOMName        { get; set; }   // C_UOM.Name, for the row's sub-line
            public int     UOMPrecision   { get; set; }
            // M_AttributeSetInstance.Description — the lot / serial / attributes
            // the line was sold against. Blank when the line carries none.
            public string  AttributeSetInstance { get; set; }
            public string  LineType       { get; set; }   // product | service | charge | other
            public string  DeliveredState { get; set; }   // full | part | none
        }

        /// <summary>
        /// One prior version of an order line (C_OrderLineHistory) — what it said
        /// before a change, and who made it. Keyed to its line by C_OrderLine_ID;
        /// the panel draws it in a drawer under that line.
        /// </summary>
        public class LineHistoryData
        {
            public int       C_OrderLine_ID { get; set; }
            public int       LineNo         { get; set; }
            public DateTime? ChangedOn      { get; set; }
            public string    UpdatedByName  { get; set; }
            // On the SELECTED unit, like the line the drawer hangs under.
            public decimal   QtyEntered     { get; set; }
            public decimal   PriceEntered   { get; set; }
            public decimal   Discount       { get; set; }
            public decimal   LineNetAmt     { get; set; }
            public string    Description    { get; set; }
            public string    UOMName        { get; set; }
            public int       UOMPrecision   { get; set; }
        }

        public class DeliveryReadinessData
        {
            public int     C_OrderLine_ID { get; set; }
            public string  ProductValue   { get; set; }
            public string  ProductName    { get; set; }
            public string  UOMName        { get; set; }   // the unit the quantities are counted in
            public string  AttributeSetInstance { get; set; }
            public string  WarehouseName  { get; set; }
            public decimal PendingQty     { get; set; }
            public decimal QtyOnHand      { get; set; }
            public string  Readiness      { get; set; }   // ready | instock | short | awaited
        }

        /// <summary>
        /// One document raised from the sales order — a shipment, an invoice or a
        /// customer receipt. <see cref="TableName"/> + <see cref="RecordId"/> are
        /// what the panel opens the record with.
        /// </summary>
        public class SalesOrderDocumentData
        {
            public string    Type        { get; set; }   // delivery | invoice | receipt
            public string    TableName   { get; set; }
            public int       RecordId    { get; set; }
            public string    DocumentNo  { get; set; }
            public string    DocStatus   { get; set; }
            public DateTime? DocDate     { get; set; }
            // Null for a document with no monetary total of its own (a shipment) —
            // distinct from a genuine zero.
            public decimal?  Amount      { get; set; }
            public decimal   DiscountAmt { get; set; }   // receipts
            public int       LineCount   { get; set; }   // shipments
            public bool      IsPaid      { get; set; }   // invoices
            public string    Extra       { get; set; }   // shipment tracking no
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
            // When the order was completed (workflow DocComplete stamp, else the
            // record's last change) — the progress line's Completed stage.
            public DateTime? CompletedDate { get; set; }
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
            public string    DeliveryRule   { get; set; }   // shipping rule A/F/L/M/O/R
            // The dictionary's own name for that value, so the panel shows exactly
            // what the order screen shows. "" when it cannot be resolved.
            public string    DeliveryRuleName { get; set; }
            public string    InvoiceRule    { get; set; }
            public string    BillToAddress  { get; set; }
            public string    ShipToAddress  { get; set; }

            // Created from
            public int       QuotationId    { get; set; }
            public string    QuotationNo    { get; set; }
            // The blanket sales order this one was released against
            // (C_Order.C_Order_Blanket). 0 / "" when it was not.
            public int       BlanketOrderId { get; set; }
            public string    BlanketOrderNo { get; set; }
            public int       OpportunityId  { get; set; }
            public string    OpportunityName { get; set; }
            public int       ProjectId      { get; set; }
            public string    ProjectNo      { get; set; }   // C_Project.Value
            public string    ProjectName    { get; set; }
            // The contract the order references. ContractTable says WHICH table it
            // is — the contract master on the header, or the service contract behind
            // a line — so the panel opens the right record.
            public int       ContractId     { get; set; }
            public string    ContractNo     { get; set; }
            public string    ContractTable  { get; set; }   // VAS_ContractMaster | C_Contract
            public int       ContractCount  { get; set; }

            // Collections
            public List<SalesOrderLineData>   Lines            { get; set; }
            public List<DeliveryReadinessData> DeliveryReadiness { get; set; }
            // Prior versions of the order lines, keyed to their line by
            // C_OrderLine_ID. Empty on a schema without C_OrderLineHistory.
            public List<LineHistoryData>      History          { get; set; }
            public List<DeliveryData>         Deliveries       { get; set; }
            public List<InvoiceData>          Invoices         { get; set; }
            // The two above merged with the order's receipts, for the Documents
            // section. They stay in their own right: the KPI strip and the
            // progress stepper are derived from them.
            public List<SalesOrderDocumentData> Documents      { get; set; }
            public List<ActivityData>         Activity         { get; set; }
            public List<NoteData>             Notes            { get; set; }
            public List<FrequencyData>        Frequencies      { get; set; }
        }
    }
}
