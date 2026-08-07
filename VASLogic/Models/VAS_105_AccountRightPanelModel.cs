/********************************************************
 * Module Name    : CRM Extension VAS
 * Purpose        : Account Right Detail Panel — data model
 * Employee Code  : VAI154
 * Date           : 09-Jun-2026
 ******************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Dynamic;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Models
{
    /// <summary>
    /// Module Name   : CRM Extension VAS
    /// Purpose       : Account Right Detail Panel — data model
    /// Chronological development:
    ///   VAI154  09-Jun-2026
    ///   VAI154  10-Jul-2026  Added product details to Contracts section
    /// </summary>
    public class VAS_105_AccountRightPanelModel
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_105_AccountRightPanelModel).FullName);

        // ─────────────────────────────────────────────────────────
        // Private helper — base-currency metadata
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches base-currency metadata for the given client from the
        /// AD_ClientInfo → C_AcctSchema → C_Currency chain.
        /// Returns a dynamic with <c>baseCurrId</c> (int), <c>currencySymbol</c> (string),
        /// <c>currencyIso</c> (string) and <c>precision</c> (int).
        /// If no row is found, only <c>baseCurrId = 0</c> is set; symbol/iso/precision
        /// are left unset so client-side fallbacks take effect.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="adClientId">AD_Client_ID for the active session.</param>
        private dynamic GetCurrencyMeta(Ctx ctx, int adClientId)
        {
            dynamic meta = new ExpandoObject();
            meta.baseCurrId = 0;

            var sb = new StringBuilder();
            sb.Append("SELECT cs.C_Currency_ID AS currency_id,");
            sb.Append("       cur.ISO_Code AS currency_iso,");
            sb.Append("       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS currency_symbol,");
            sb.Append("       cur.StdPrecision AS std_precision");
            sb.Append("  FROM AD_ClientInfo ci");
            sb.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
            sb.Append("  INNER JOIN C_Currency cur ON (cur.C_Currency_ID = cs.C_Currency_ID)");
            sb.Append(" WHERE ci.AD_Client_ID = @adClientId");

            string baseSql = sb.ToString();
            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql, "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var sqlParams = new SqlParameter[]
            {
                new SqlParameter("@adClientId", adClientId)
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow row = ds.Tables[0].Rows[0];
                    meta.baseCurrId = Util.GetValueOfInt(row["currency_id"]);

                    string sym = Util.GetValueOfString(row["currency_symbol"]);
                    if (!string.IsNullOrWhiteSpace(sym))
                        meta.currencySymbol = sym;

                    string iso = Util.GetValueOfString(row["currency_iso"]);
                    if (!string.IsNullOrWhiteSpace(iso))
                        meta.currencyIso = iso;

                    if (row["std_precision"] != DBNull.Value)
                        meta.precision = Util.GetValueOfInt(row["std_precision"]);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetCurrencyMeta", ex.Message);
            }

            return meta;
        }

        // ─────────────────────────────────────────────────────────
        // §1  Overview
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns account header, company facts, primary contact, annual revenue,
        /// days-to-renewal, open-opportunity count, and base-currency metadata for
        /// the specified C_BPartner record.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <returns>
        /// Dynamic object with fields: <c>id</c>, <c>accountCode</c>, <c>name</c>,
        /// <c>website</c>, <c>employees</c>, <c>industry</c>, <c>segment</c>,
        /// <c>region</c>, <c>owner</c>, <c>tier</c>, <c>contactName</c>,
        /// <c>contactTitle</c>, <c>contactEmail</c>, <c>contactPhone</c>,
        /// <c>annualRevenue</c>, <c>renewalInDays</c>, <c>openOpportunities</c>,
        /// <c>currencySymbol</c>, <c>currencyIso</c>, <c>precision</c>,
        /// <c>baseCurrId</c>, <c>error</c>.
        /// </returns>
        public dynamic GetOverview(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            response.error = null;

            int adClientId = ctx.GetAD_Client_ID();

            // ── Base currency metadata ────────────────────────────────────────
            try
            {
                dynamic currMeta = GetCurrencyMeta(ctx, adClientId);
                response.baseCurrId = currMeta.baseCurrId;
                var metaDict = (IDictionary<string, object>)currMeta;
                if (metaDict.ContainsKey("currencySymbol")) response.currencySymbol = currMeta.currencySymbol;
                if (metaDict.ContainsKey("currencyIso")) response.currencyIso = currMeta.currencyIso;
                if (metaDict.ContainsKey("precision")) response.precision = currMeta.precision;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetOverview.CurrencyMeta", ex.Message);
            }

            // ── Main C_BPartner row — core fields only, no extension columns ──
            // Extension columns (C_IndustryCode_ID, VA051_Stage, VAS_ProjectStatus,
            // CancellationDate) are intentionally excluded here; they are fetched
            // individually below so a missing column only silences that one KPI
            // rather than aborting the entire response.
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT bp.C_BPartner_ID AS id,");
                sb.Append("       bp.Value AS account_code,");
                sb.Append("       bp.Name AS name,");
                sb.Append("       bp.URL AS website,");
                sb.Append("       bp.NumberEmployees AS employees,");
                sb.Append("       g.Name AS segment,");
                sb.Append("       rep.Name AS owner,");
                sb.Append("       CASE bp.Rating");
                sb.Append("           WHEN 'A' THEN 'Platinum'");
                sb.Append("           WHEN 'B' THEN 'Gold'");
                sb.Append("           ELSE 'Silver'");
                sb.Append("       END AS tier,");
                sb.Append("       u.Name AS contact_name,");
                sb.Append("       u.Title AS contact_title,");
                sb.Append("       u.EMail AS contact_email,");
                sb.Append("       u.Phone AS contact_phone,");
                sb.Append("       sr.Name AS region");
                sb.Append("  FROM C_BPartner bp");
                sb.Append("  LEFT OUTER JOIN C_BP_Group g    ON (g.C_BP_Group_ID    = bp.C_BP_Group_ID    AND g.IsActive  = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_SalesRegion sr ON (sr.C_SalesRegion_ID = bp.C_SalesRegion_ID AND sr.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN AD_User rep     ON (rep.AD_User_ID      = bp.SalesRep_ID      AND rep.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN AD_User u       ON (u.C_BPartner_ID     = bp.C_BPartner_ID    AND u.IsActive  = 'Y'");
                sb.Append("                                      AND u.AD_User_ID = (SELECT MIN(u2.AD_User_ID) FROM AD_User u2 WHERE u2.C_BPartner_ID = bp.C_BPartner_ID AND u2.IsActive = 'Y'))");
                // Single-record lookup by primary key: IsActive intentionally omitted so that
                // inactive/archived BPartners opened via the parent form still show their overview.
                // Client/org security is enforced by AddAccessSQL below.
                sb.Append(" WHERE bp.C_BPartner_ID = @bPartnerId");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var sqlParams = new SqlParameter[] { new SqlParameter("@bPartnerId", bPartnerId) };

                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                {
                    response.error = "not_found";
                    return response;
                }

                DataRow row = ds.Tables[0].Rows[0];
                response.id = Util.GetValueOfInt(row["id"]);
                response.accountCode = Util.GetValueOfString(row["account_code"]);
                response.name = Util.GetValueOfString(row["name"]);
                response.website = Util.GetValueOfString(row["website"]);
                response.employees = row["employees"] != DBNull.Value ? Util.GetValueOfInt(row["employees"]) : 0;
                response.industry = string.Empty;
                response.segment = Util.GetValueOfString(row["segment"]);
                response.region = Util.GetValueOfString(row["region"]);
                response.owner = Util.GetValueOfString(row["owner"]);
                response.tier = Util.GetValueOfString(row["tier"]);
                response.contactName = Util.GetValueOfString(row["contact_name"]);
                response.contactTitle = Util.GetValueOfString(row["contact_title"]);
                response.contactEmail = Util.GetValueOfString(row["contact_email"]);
                response.contactPhone = Util.GetValueOfString(row["contact_phone"]);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetOverview.Main", ex.Message);
                response.error = "not_found";
                return response;
            }

            // ── KPI: annual revenue (3-yr avg from invoices, base-currency converted) ────
            response.annualRevenue = 0m;
            try
            {
                DateTime arr3Yr = DateTime.Today.AddYears(-3);
                var sbArr = new StringBuilder();
                // Fetch each invoice's converted amount individually (same pattern as GetInvoices)
                // so CURRENCYCONVERT runs per-row under DataSet type mapping, avoiding SUM cast issues.
                sbArr.Append("SELECT CURRENCYCONVERT(inv.GrandTotal, inv.C_Currency_ID, cs.C_Currency_ID,");
                sbArr.Append("           COALESCE(inv.DateAcct, inv.DateInvoiced), inv.C_ConversionType_ID,");
                sbArr.Append("           inv.AD_Client_ID, inv.AD_Org_ID) AS conv_amount");
                sbArr.Append("  FROM C_Invoice inv");
                sbArr.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = inv.AD_Client_ID)");
                sbArr.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                sbArr.Append("  INNER JOIN C_DocType dt ON (dt.C_DocType_ID = inv.C_DocType_ID)");
                sbArr.Append(" WHERE inv.IsActive = 'Y' AND inv.IsSOTrx = 'Y'");
                sbArr.Append("   AND inv.DocStatus IN ('CO','CL')");
                sbArr.Append("   AND dt.DocBaseType IN ('ARI','ARC')");
                sbArr.Append("   AND inv.C_BPartner_ID = @bpArr");
                sbArr.Append("   AND COALESCE(inv.DateAcct, inv.DateInvoiced) >= @arr3Yr");

                string arrAccess = MRole.GetDefault(ctx).AddAccessSQL(
                    sbArr.ToString(), "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var arrParams = new SqlParameter[]
                {
                    new SqlParameter("@bpArr",  bPartnerId),
                    new SqlParameter("@arr3Yr", arr3Yr)
                };

                decimal totalRevenue = 0m;
                DataSet arrDs = DB.ExecuteDataset(arrAccess, arrParams, null);
                if (arrDs != null && arrDs.Tables.Count > 0)
                {
                    foreach (DataRow r in arrDs.Tables[0].Rows)
                    {
                        if (r["conv_amount"] != DBNull.Value)
                            totalRevenue += Convert.ToDecimal(r["conv_amount"]);
                    }
                }
                response.annualRevenue = totalRevenue / 3m;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetOverview.AnnualRevenue", ex.Message);
            }

            // ── KPI: open opportunities count ─────────────────────────────────
            response.openOpportunities = 0;
            try
            {
                var sbOpp = new StringBuilder();
                sbOpp.Append("SELECT COUNT(*) AS opp_cnt");
                sbOpp.Append("  FROM VAS_Opportunity op");
                sbOpp.Append(" WHERE op.IsActive = 'Y'");
                sbOpp.Append("   AND (op.C_BPartner_ID = @bpOpp OR op.Ref_BPartner_ID = @bpOpp2)");

                string oppAccess = MRole.GetDefault(ctx).AddAccessSQL(
                    sbOpp.ToString(), "op", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var oppParams = new SqlParameter[]
                {
                    new SqlParameter("@bpOpp",  bPartnerId),
                    new SqlParameter("@bpOpp2", bPartnerId)
                };
                object oppObj = DB.ExecuteScalar(oppAccess, oppParams, null);
                if (oppObj != null && oppObj != DBNull.Value)
                    response.openOpportunities = Util.GetValueOfInt(oppObj);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetOverview.OpenOpps", ex.Message);
            }

            // ── KPI: renewal in days (from C_Contract) ────────────────────────
            response.renewalInDays = null;
            try
            {
                var sbRen = new StringBuilder();
                sbRen.Append("SELECT TO_CHAR(MIN(ct.EndDate),'YYYY-MM-DD') AS next_end");
                sbRen.Append("  FROM C_Contract ct");
                sbRen.Append(" WHERE ct.IsActive = 'Y' AND ct.C_BPartner_ID = @bpRen");
                sbRen.Append("   AND ct.DocStatus IN ('CO','CL')");
                sbRen.Append("   AND ct.EndDate >= CURRENT_DATE");

                string renAccess = MRole.GetDefault(ctx).AddAccessSQL(
                    sbRen.ToString(), "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var renParams = new SqlParameter[] { new SqlParameter("@bpRen", bPartnerId) };
                object renObj = DB.ExecuteScalar(renAccess, renParams, null);
                if (renObj != null && renObj != DBNull.Value)
                {
                    DateTime endDt;
                    if (DateTime.TryParse(renObj.ToString(), out endDt))
                        response.renewalInDays = (int)(endDt.Date - DateTime.Today).TotalDays;
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetOverview.RenewalDays", ex.Message);
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §2  Contacts
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active contacts linked to the specified C_BPartner.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <returns>
        /// Dynamic object with <c>items</c> (list of contacts) and <c>total</c> (int).
        /// Each item exposes: <c>id</c>, <c>name</c>, <c>title</c>, <c>email</c>, <c>phone</c>.
        /// </returns>
        public dynamic GetContacts(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            response.total = 0;

            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT u.AD_User_ID AS id,");
                // COALESCE guards against NULL LastName so the concatenated name is never NULL
                sb.Append("       TRIM(COALESCE(u.Name,N'') || ' ' || COALESCE(u.LastName,N'')) AS name,");
                // LEFT OUTER JOIN so contacts without a job entry are still included
                sb.Append("       COALESCE(j.Name, u.Title, N'') AS title,");
                sb.Append("       u.EMail AS email,");
                sb.Append("       u.Phone AS phone");
                sb.Append("  FROM AD_User u");
                sb.Append("  LEFT OUTER JOIN C_Job j ON (j.C_Job_ID = u.C_Job_ID AND j.IsActive = 'Y')");
                sb.Append(" WHERE u.IsActive = 'Y' AND u.C_BPartner_ID = @bPartnerId");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "u", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // ORDER BY appended after AddAccessSQL (RULE 5)
                accessSql += " ORDER BY u.Name";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@bPartnerId", bPartnerId)
                };

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.id = Util.GetValueOfInt(row["id"]);
                        item.name = Util.GetValueOfString(row["name"]);
                        item.title = Util.GetValueOfString(row["title"]);
                        item.email = Util.GetValueOfString(row["email"]);
                        item.phone = Util.GetValueOfString(row["phone"]);
                        items.Add(item);
                    }
                    response.total = items.Count;
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetContacts", ex.Message);
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §3  Locations
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active locations (ship-to / bill-to addresses) for the
        /// specified C_BPartner.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <returns>
        /// Dynamic object with <c>items</c> (list) and <c>total</c> (int).
        /// Each item exposes: <c>id</c>, <c>siteName</c>, <c>address</c>, <c>city</c>,
        /// <c>postal</c>, <c>regionName</c>, <c>country</c>, <c>locType</c>,
        /// <c>primaryContact</c>.
        /// </returns>
        public dynamic GetLocations(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            response.total = 0;

            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT bpl.C_BPartner_Location_ID AS id,");
                sb.Append("       bpl.Name AS site_name,");
                sb.Append("       TRIM(COALESCE(l.Address1,N'') || ' ' || COALESCE(l.Address2,N'')) AS address,");
                sb.Append("       l.City AS city,");
                sb.Append("       l.Postal AS postal,");
                sb.Append("       l.RegionName AS region_name,");
                sb.Append("       co.Name AS country,");
                sb.Append("       CASE WHEN bpl.IsShipTo = 'Y' THEN 'VAS_105_ShipTo'");
                sb.Append("            WHEN bpl.IsBillTo = 'Y' THEN 'VAS_105_BillTo'");
                sb.Append("            ELSE 'VAS_105_Site' END AS loc_type,");
                // Scalar subquery for primary contact — avoids duplicate rows from a JOIN
                sb.Append("       (SELECT MIN(uu.Name) FROM AD_User uu WHERE uu.C_BPartner_Location_ID = bpl.C_BPartner_Location_ID AND uu.IsActive = 'Y') AS primary_contact");
                // Drive FROM C_BPartner_Location directly; the old AD_USER INNER JOIN excluded locations without linked users
                sb.Append("  FROM C_BPartner_Location bpl");
                sb.Append("  LEFT OUTER JOIN C_Location l ON (l.C_Location_ID = bpl.C_Location_ID)");
                sb.Append("  LEFT OUTER JOIN C_Country co ON (co.C_Country_ID = l.C_Country_ID)");
                sb.Append(" WHERE bpl.IsActive = 'Y' AND bpl.C_BPartner_ID = @bPartnerId");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "bpl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // ORDER BY appended after AddAccessSQL (RULE 5)
                accessSql += " ORDER BY bpl.Name";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@bPartnerId", bPartnerId)
                };

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.id = Util.GetValueOfInt(row["id"]);
                        item.siteName = Util.GetValueOfString(row["site_name"]);
                        item.address = Util.GetValueOfString(row["address"]);
                        item.city = Util.GetValueOfString(row["city"]);
                        item.postal = Util.GetValueOfString(row["postal"]);
                        item.regionName = Util.GetValueOfString(row["region_name"]);
                        item.country = Util.GetValueOfString(row["country"]);
                        item.locType = Msg.GetMsg(ctx, Util.GetValueOfString(row["loc_type"]));
                        item.primaryContact = Util.GetValueOfString(row["primary_contact"]);
                        items.Add(item);
                    }
                    response.total = items.Count;
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetLocations", ex.Message);
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §4  Opportunities
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active sales opportunities linked to the specified C_BPartner,
        /// with values converted to the client's base currency.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <returns>
        /// Dynamic object with <c>items</c>, <c>total</c> (int), and base-currency
        /// metadata (<c>currencySymbol</c>, <c>currencyIso</c>, <c>precision</c>,
        /// <c>baseCurrId</c>).
        /// Each item exposes: <c>id</c>, <c>name</c>, <c>oppCode</c>, <c>stageCode</c>,
        /// <c>probability</c>, <c>closeDate</c>, <c>owner</c>, <c>value</c>.
        /// </returns>
        public dynamic GetOpportunities(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            response.total = 0;

            int adClientId = ctx.GetAD_Client_ID();

            try
            {
                dynamic currMeta = GetCurrencyMeta(ctx, adClientId);
                response.baseCurrId = currMeta.baseCurrId;

                var metaDict = (IDictionary<string, object>)currMeta;
                if (metaDict.ContainsKey("currencySymbol"))
                    response.currencySymbol = currMeta.currencySymbol;
                if (metaDict.ContainsKey("currencyIso"))
                    response.currencyIso = currMeta.currencyIso;
                if (metaDict.ContainsKey("precision"))
                    response.precision = currMeta.precision;

                string lang = ctx.GetAD_Language();

                var sb = new StringBuilder();
                sb.Append("SELECT vo.VAS_Opportunity_ID AS id,");
                sb.Append("       vo.Name AS name,");
                sb.Append("       vo.Value AS opp_code,");
                sb.Append("       vo.VAS_OppStage AS stage_code,");
                sb.Append("       (SELECT COALESCE(trl.Name, r.Name)");
                sb.Append("          FROM AD_Ref_List r");
                sb.Append("          LEFT OUTER JOIN AD_Ref_List_Trl trl ON (trl.AD_Ref_List_ID = r.AD_Ref_List_ID");
                sb.Append("               AND trl.IsActive = 'Y' AND trl.AD_Language = @lang)");
                sb.Append("         WHERE r.IsActive = 'Y'");
                sb.Append("           AND r.Value = vo.VAS_OppStage");
                sb.Append("           AND r.AD_Reference_ID IN (SELECT col.AD_Reference_Value_ID");
                sb.Append("                                        FROM AD_Column col");
                sb.Append("                                       INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID = col.AD_Table_ID");
                sb.Append("                                            AND tbl.TableName = 'VAS_Opportunity'");
                sb.Append("                                            AND tbl.IsActive = 'Y')");
                sb.Append("                                       WHERE col.ColumnName = 'VAS_OppStage'");
                sb.Append("                                         AND col.IsActive = 'Y')) AS stage_name,");
                sb.Append("       vo.Probability AS probability,");
                sb.Append("       TO_CHAR(vo.VAS_DecisionDate,'YYYY-MM-DD') AS close_date,");
                sb.Append("       rep.Name AS owner,");
                sb.Append("       CURRENCYCONVERT(vo.PlannedAmt, vo.C_Currency_ID, cs.C_Currency_ID,");
                sb.Append("           COALESCE(vo.VAS_DecisionDate, CURRENT_DATE), NULL,");
                sb.Append("           vo.AD_Client_ID, vo.AD_Org_ID) AS value");
                sb.Append("  FROM VAS_Opportunity vo");
                sb.Append("  LEFT OUTER JOIN AD_User rep ON (rep.AD_User_ID = vo.SalesRep_ID AND rep.IsActive = 'Y')");
                sb.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = vo.AD_Client_ID)");
                sb.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                sb.Append(" WHERE vo.IsActive = 'Y'");
                sb.Append("   AND (vo.C_BPartner_ID = @bPartnerId OR vo.Ref_BPartner_ID = @bPartnerId2)");
                sb.Append("   AND vo.VAS_OppStage NOT IN ('16', '17')");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "vo", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // ORDER BY appended after AddAccessSQL (RULE 5)
                accessSql += " ORDER BY vo.VAS_DecisionDate DESC";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@lang",        lang),
                    new SqlParameter("@bPartnerId",  bPartnerId),
                    new SqlParameter("@bPartnerId2", bPartnerId)
                };

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.id = Util.GetValueOfInt(row["id"]);
                        item.name = Util.GetValueOfString(row["name"]);
                        item.oppCode = Util.GetValueOfString(row["opp_code"]);
                        item.stageCode = Util.GetValueOfString(row["stage_code"]);
                        item.stageName = row["stage_name"] != DBNull.Value ? Util.GetValueOfString(row["stage_name"]) : "";
                        item.probability = row["probability"] != DBNull.Value
                            ? Convert.ToDecimal(row["probability"]) : 0m;
                        item.closeDate = Util.GetValueOfString(row["close_date"]);
                        item.owner = Util.GetValueOfString(row["owner"]);
                        item.value = row["value"] != DBNull.Value
                            ? Convert.ToDecimal(row["value"]) : 0m;
                        items.Add(item);
                    }
                    response.total = items.Count;
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetOpportunities", ex.Message);
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §5  Contracts
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active contracts for the specified C_BPartner, with values
        /// converted to the client's base currency.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <returns>
        /// Dynamic object with <c>items</c> and base-currency metadata.
        /// Each item exposes: <c>id</c>, <c>contractNo</c>, <c>name</c>,
        /// <c>typeCode</c>, <c>startDate</c>, <c>endDate</c>, <c>statusCode</c>,
        /// <c>renewalCode</c>, <c>value</c>, <c>productName</c>,
        /// <c>source</c> ("CC" = C_Contract, "VM" = VAS_ContractMaster).
        /// </returns>
        public dynamic GetContracts(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();

            int adClientId = ctx.GetAD_Client_ID();
            string lang    = ctx.GetAD_Language();

            try
            {
                dynamic currMeta = GetCurrencyMeta(ctx, adClientId);
                response.baseCurrId = currMeta.baseCurrId;

                var metaDict = (IDictionary<string, object>)currMeta;
                if (metaDict.ContainsKey("currencySymbol"))
                    response.currencySymbol = currMeta.currencySymbol;
                if (metaDict.ContainsKey("currencyIso"))
                    response.currencyIso = currMeta.currencyIso;
                if (metaDict.ContainsKey("precision"))
                    response.precision = currMeta.precision;

                var items = (List<dynamic>)response.items;

                // ── Part 1: C_Contract ────────────────────────────────────────────
                try
                {
                    var sbCC = new StringBuilder();
                    sbCC.Append("SELECT ct.C_Contract_ID AS id,");
                    sbCC.Append("       ct.DocumentNo AS contract_no,");
                    sbCC.Append("       ct.Description AS ct_description,");
                    sbCC.Append("       ct.ContractType AS type_code,");
                    sbCC.Append("       TO_CHAR(ct.StartDate,'YYYY-MM-DD') AS start_date,");
                    sbCC.Append("       TO_CHAR(ct.EndDate,'YYYY-MM-DD') AS end_date,");
                    sbCC.Append("       ct.Processed AS status_code,");
                    sbCC.Append("       ct.RenewalType AS renewal_code,");
                    sbCC.Append("       (SELECT COALESCE(trl.Name, r.Name)");
                    sbCC.Append("          FROM AD_Ref_List r");
                    sbCC.Append("          LEFT OUTER JOIN AD_Ref_List_Trl trl ON (trl.AD_Ref_List_ID = r.AD_Ref_List_ID");
                    sbCC.Append("               AND trl.IsActive = 'Y' AND trl.AD_Language = @ctLang)");
                    sbCC.Append("         WHERE r.IsActive = 'Y'");
                    sbCC.Append("           AND r.Value = ct.RenewalType");
                    sbCC.Append("           AND r.AD_Reference_ID IN (SELECT col.AD_Reference_Value_ID");
                    sbCC.Append("                                        FROM AD_Column col");
                    sbCC.Append("                                       INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID = col.AD_Table_ID");
                    sbCC.Append("                                            AND tbl.TableName = 'C_Contract'");
                    sbCC.Append("                                            AND tbl.IsActive = 'Y')");
                    sbCC.Append("                                       WHERE col.ColumnName = 'RenewalType'");
                    sbCC.Append("                                         AND col.IsActive = 'Y')) AS renewal_name,");
                    // COALESCE ensures the raw amount is shown when no conversion rate exists
                    sbCC.Append("       COALESCE(CURRENCYCONVERT(ct.GrandTotal, ct.C_Currency_ID, cs.C_Currency_ID,");
                    sbCC.Append("           COALESCE(ct.StartDate, CURRENT_DATE), NULL,");
                    sbCC.Append("           ct.AD_Client_ID, ct.AD_Org_ID), ct.GrandTotal) AS value,");
                    sbCC.Append("       p.Name AS product_name");
                    sbCC.Append("  FROM C_Contract ct");
                    sbCC.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = ct.AD_Client_ID)");
                    sbCC.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                    sbCC.Append("  LEFT OUTER JOIN M_Product p ON (p.M_Product_ID = ct.M_Product_ID)");
                    sbCC.Append(" WHERE ct.IsActive = 'Y' AND ct.C_BPartner_ID = @bPartnerIdCC");

                    string accessCC = MRole.GetDefault(ctx).AddAccessSQL(
                        sbCC.ToString(), "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    accessCC += " ORDER BY ct.StartDate DESC";

                    var paramsCC = new SqlParameter[] {
                        new SqlParameter("@bPartnerIdCC", bPartnerId),
                        new SqlParameter("@ctLang",       lang)
                    };
                    DataSet dsCC = DB.ExecuteDataset(accessCC, paramsCC, null);
                    if (dsCC != null && dsCC.Tables.Count > 0)
                    {
                        foreach (DataRow row in dsCC.Tables[0].Rows)
                        {
                            dynamic item = new ExpandoObject();
                            item.id          = Util.GetValueOfInt(row["id"]);
                            item.contractNo  = Util.GetValueOfString(row["contract_no"]);
                            item.name        = "";  // C_Contract: DocumentNo is the display title; use fallback in JS
                            item.description = Util.GetValueOfString(row["ct_description"]);
                            item.typeCode    = Util.GetValueOfString(row["type_code"]);
                            item.startDate   = Util.GetValueOfString(row["start_date"]);
                            item.endDate     = Util.GetValueOfString(row["end_date"]);
                            item.statusCode  = Util.GetValueOfString(row["status_code"]);
                            item.renewalCode = Util.GetValueOfString(row["renewal_code"]);
                            item.renewalName = row["renewal_name"] != DBNull.Value ? Util.GetValueOfString(row["renewal_name"]) : "";
                            item.value       = row["value"] != DBNull.Value ? Convert.ToDecimal(row["value"]) : 0m;
                            item.productName = Util.GetValueOfString(row["product_name"]);
                            item.source      = "CC";
                            items.Add(item);
                        }
                    }
                }
                catch (Exception exCC)
                {
                    _log.SaveError("VAS_105_AccountRightPanelModel.GetContracts.C_Contract", exCC.Message);
                }

                // ── Part 2: VAS_ContractMaster ────────────────────────────────────
                try
                {
                    var sbVM = new StringBuilder();
                    sbVM.Append("SELECT vm.VAS_ContractMaster_ID AS id,");
                    sbVM.Append("       vm.DocumentNo AS contract_no,");
                    sbVM.Append("       vm.VAS_ContractSummary AS name,");
                    sbVM.Append("       vm.ContractType AS type_code,");
                    sbVM.Append("       TO_CHAR(vm.StartDate,'YYYY-MM-DD') AS start_date,");
                    sbVM.Append("       TO_CHAR(vm.EndDate,'YYYY-MM-DD') AS end_date,");
                    sbVM.Append("       vm.VAS_Status AS status_code,");
                    sbVM.Append("       vm.RenewalType AS renewal_code,");
                    sbVM.Append("       (SELECT COALESCE(trl.Name, r.Name)");
                    sbVM.Append("          FROM AD_Ref_List r");
                    sbVM.Append("          LEFT OUTER JOIN AD_Ref_List_Trl trl ON (trl.AD_Ref_List_ID = r.AD_Ref_List_ID");
                    sbVM.Append("               AND trl.IsActive = 'Y' AND trl.AD_Language = @vmLang)");
                    sbVM.Append("         WHERE r.IsActive = 'Y'");
                    sbVM.Append("           AND r.Value = vm.RenewalType");
                    sbVM.Append("           AND r.AD_Reference_ID IN (SELECT col.AD_Reference_Value_ID");
                    sbVM.Append("                                        FROM AD_Column col");
                    sbVM.Append("                                       INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID = col.AD_Table_ID");
                    sbVM.Append("                                            AND tbl.TableName = 'VAS_ContractMaster'");
                    sbVM.Append("                                            AND tbl.IsActive = 'Y')");
                    sbVM.Append("                                       WHERE col.ColumnName = 'RenewalType'");
                    sbVM.Append("                                         AND col.IsActive = 'Y')) AS renewal_name,");
                    // COALESCE ensures the raw amount is shown when no conversion rate exists
                    sbVM.Append("       COALESCE(CURRENCYCONVERT(vm.VAS_ContractAmount, vm.C_Currency_ID, cs.C_Currency_ID,");
                    sbVM.Append("           COALESCE(vm.StartDate, CURRENT_DATE), NULL,");
                    sbVM.Append("           vm.AD_Client_ID, vm.AD_Org_ID), vm.VAS_ContractAmount) AS value");
                    sbVM.Append("  FROM VAS_ContractMaster vm");
                    sbVM.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = vm.AD_Client_ID)");
                    sbVM.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                    sbVM.Append(" WHERE vm.IsActive = 'Y' AND vm.C_BPartner_ID = @bPartnerIdVM");

                    string accessVM = MRole.GetDefault(ctx).AddAccessSQL(
                        sbVM.ToString(), "vm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    accessVM += " ORDER BY vm.StartDate DESC";

                    var paramsVM = new SqlParameter[] {
                        new SqlParameter("@bPartnerIdVM", bPartnerId),
                        new SqlParameter("@vmLang",       lang)
                    };
                    DataSet dsVM = DB.ExecuteDataset(accessVM, paramsVM, null);
                    if (dsVM != null && dsVM.Tables.Count > 0)
                    {
                        foreach (DataRow row in dsVM.Tables[0].Rows)
                        {
                            dynamic item = new ExpandoObject();
                            item.id          = Util.GetValueOfInt(row["id"]);
                            item.contractNo  = Util.GetValueOfString(row["contract_no"]);
                            item.name        = Util.GetValueOfString(row["name"]);
                            item.description = "";  // VAS_ContractMaster has no Description column; VAS_ContractSummary is used as name
                            item.typeCode    = Util.GetValueOfString(row["type_code"]);
                            item.startDate   = Util.GetValueOfString(row["start_date"]);
                            item.endDate     = Util.GetValueOfString(row["end_date"]);
                            item.statusCode  = Util.GetValueOfString(row["status_code"]);
                            item.renewalCode = Util.GetValueOfString(row["renewal_code"]);
                            item.renewalName = row["renewal_name"] != DBNull.Value ? Util.GetValueOfString(row["renewal_name"]) : "";
                            item.value       = row["value"] != DBNull.Value ? Convert.ToDecimal(row["value"]) : 0m;
                            item.productName = "";
                            item.source      = "VM";
                            items.Add(item);
                        }
                    }
                }
                catch (Exception exVM)
                {
                    _log.SaveError("VAS_105_AccountRightPanelModel.GetContracts.VAS_ContractMaster", exVM.Message);
                }

                // ── Part 2b: Enrich VAS_ContractMaster items with product names from VAS_ContractLine ──
                try
                {
                    // Collect VAS_ContractMaster IDs from the items already fetched
                    var vmIds = new List<int>();
                    foreach (dynamic it in items)
                    {
                        var dict = (IDictionary<string, object>)it;
                        if ((string)dict["source"] == "VM")
                            vmIds.Add((int)dict["id"]);
                    }

                    if (vmIds.Count > 0)
                    {
                        // Build a comma-separated integer IN list — these are internal DB IDs, not user input
                        var sbIds = new StringBuilder();
                        for (int k = 0; k < vmIds.Count; k++)
                        {
                            if (k > 0) sbIds.Append(", ");
                            sbIds.Append(vmIds[k]);
                        }

                        var sbProd = new StringBuilder();
                        sbProd.Append("SELECT vl.VAS_ContractMaster_ID AS master_id,");
                        sbProd.Append("       p.Name AS product_name");
                        sbProd.Append("  FROM VAS_ContractLine vl");
                        sbProd.Append("  INNER JOIN M_Product p ON (p.M_Product_ID = vl.M_Product_ID)");
                        sbProd.Append(" WHERE vl.IsActive = 'Y' AND vl.M_Product_ID > 0");
                        sbProd.Append("   AND vl.VAS_ContractMaster_ID IN (");
                        sbProd.Append(sbIds);
                        sbProd.Append(") ORDER BY vl.VAS_ContractMaster_ID, p.Name");

                        DataSet dsProd = DB.ExecuteDataset(sbProd.ToString(), null, null);

                        // Aggregate product names per contract master
                        var productMap = new Dictionary<int, List<string>>();
                        if (dsProd != null && dsProd.Tables.Count > 0)
                        {
                            foreach (DataRow row in dsProd.Tables[0].Rows)
                            {
                                int masterId  = Util.GetValueOfInt(row["master_id"]);
                                string pName  = Util.GetValueOfString(row["product_name"]);
                                if (!productMap.ContainsKey(masterId))
                                    productMap[masterId] = new List<string>();
                                if (!string.IsNullOrEmpty(pName) && !productMap[masterId].Contains(pName))
                                    productMap[masterId].Add(pName);
                            }
                        }

                        // Apply aggregated product names back to the VM items
                        foreach (dynamic it in items)
                        {
                            var dict = (IDictionary<string, object>)it;
                            if ((string)dict["source"] == "VM")
                            {
                                int masterId = (int)dict["id"];
                                if (productMap.ContainsKey(masterId))
                                    it.productName = string.Join(", ", productMap[masterId]);
                            }
                        }
                    }
                }
                catch (Exception exProd)
                {
                    _log.SaveError("VAS_105_AccountRightPanelModel.GetContracts.ProductNames", exProd.Message);
                }

                // Sort merged list by StartDate descending (YYYY-MM-DD string order == date order)
                items.Sort((a, b) => string.Compare(
                    (string)(((IDictionary<string, object>)b)["startDate"] ?? ""),
                    (string)(((IDictionary<string, object>)a)["startDate"] ?? ""),
                    StringComparison.Ordinal));
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetContracts", ex.Message);
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §6  Tickets  (paged, state filter)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns support tickets for the specified C_BPartner filtered by state.
        /// Open tickets are returned in full (no pagination); past tickets are paged.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <param name="state">"open" for active tickets; "past" for resolved/closed tickets.</param>
        /// <param name="pageOffset">Zero-based row offset — applies to "past" state only.</param>
        /// <param name="pageSize">Page size — applies to "past" state only.</param>
        /// <returns>
        /// Dynamic object with <c>items</c>, <c>total</c> (count of past tickets only),
        /// and <c>state</c>.
        /// Each item exposes: <c>id</c>, <c>ticketNo</c>, <c>subject</c>,
        /// <c>priorityCode</c>, <c>ticketType</c>, <c>status</c>, <c>opened</c>,
        /// <c>ageDays</c>, <c>contact</c>.
        /// </returns>
        public dynamic GetTickets(Ctx ctx, int bPartnerId, string state, int pageOffset, int pageSize)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            response.total = 0;
            response.state = state;

            bool isPast = (state == "past");

            // Fetch Priority display names from AD reference list for this session language
            var priorityNames = GetRefListNames(ctx, "Priority", "R_Request");

            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT r.R_Request_ID AS id,");
                sb.Append("       r.DocumentNo AS ticket_no,");
                sb.Append("       r.Summary AS subject,");
                sb.Append("       r.Priority AS priority_code,");
                sb.Append("       rs.Name AS status,");
                sb.Append("       TO_CHAR(r.Created,'YYYY-MM-DD') AS opened,");
                sb.Append("       CAST(DAYSBETWEEN( CURRENT_DATE,r.Created) AS INTEGER) AS age_days,");
                sb.Append("       u.Name AS contact,");
                sb.Append("       r.DateNextAction AS date_next_action,");
                sb.Append("       CAST(DAYSBETWEEN(r.Updated, CURRENT_DATE) AS INTEGER) AS inactive_days");
                sb.Append("  FROM R_Request r");
                sb.Append("  LEFT OUTER JOIN R_Status rs ON (rs.R_Status_ID = r.R_Status_ID)");
                sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = r.CreatedBy)");
                sb.Append(" WHERE r.IsActive = 'Y' AND r.C_BPartner_ID = @bPartnerId");

                if (isPast)
                    sb.Append(" AND r.Processed = 'Y'");
                else
                    sb.Append(" AND COALESCE(r.Processed,'N') = 'N'");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // ORDER BY / pagination appended after AddAccessSQL (RULE 5)
                // Open tickets: top 5 by priority (High=1 first), then oldest first.
                if (isPast)
                    accessSql += " ORDER BY r.Updated DESC OFFSET @pageOffset ROWS FETCH NEXT @pageSize ROWS ONLY";
                else
                    accessSql += " ORDER BY r.Priority ASC, r.Created ASC FETCH FIRST 5 ROWS ONLY";

                SqlParameter[] sqlParams;
                if (isPast)
                {
                    sqlParams = new SqlParameter[]
                    {
                        new SqlParameter("@bPartnerId", bPartnerId),
                        new SqlParameter("@pageOffset", pageOffset),
                        new SqlParameter("@pageSize",   pageSize)
                    };
                }
                else
                {
                    sqlParams = new SqlParameter[]
                    {
                        new SqlParameter("@bPartnerId", bPartnerId)
                    };
                }

                // For past tickets also fetch total count
                if (isPast)
                {
                    var countSb = new StringBuilder();
                    countSb.Append("SELECT COUNT(*)");
                    countSb.Append("  FROM R_Request r");
                    countSb.Append(" WHERE r.IsActive = 'Y' AND r.C_BPartner_ID = @bpIdCount");
                    countSb.Append("   AND r.Processed = 'Y'");

                    string countBase = countSb.ToString();
                    string countAccess = MRole.GetDefault(ctx).AddAccessSQL(
                        countBase, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    var countParams = new SqlParameter[]
                    {
                        new SqlParameter("@bpIdCount", bPartnerId)
                    };

                    object countObj = DB.ExecuteScalar(countAccess, countParams, null);
                    response.total = countObj != null ? Util.GetValueOfInt(countObj) : 0;
                }

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.id = Util.GetValueOfInt(row["id"]);
                        item.ticketNo = Util.GetValueOfString(row["ticket_no"]);
                        item.subject = Util.GetValueOfString(row["subject"]);
                        string prioCode = Util.GetValueOfString(row["priority_code"]);
                        item.priorityCode = prioCode;
                        item.priorityName = priorityNames.ContainsKey(prioCode)
                            ? priorityNames[prioCode] : prioCode;
                        item.status = Util.GetValueOfString(row["status"]);
                        item.opened = Util.GetValueOfString(row["opened"]);
                        item.ageDays = row["age_days"] != DBNull.Value
                            ? Util.GetValueOfInt(row["age_days"]) : 0;
                        item.contact = Util.GetValueOfString(row["contact"]);

                        // ── Red-indicator flags ──────────────────────────────
                        // isOverdue: DateNextAction is set and has already passed.
                        bool isOverdue = false;
                        object dnaRaw = row["date_next_action"];
                        if (dnaRaw != null && dnaRaw != DBNull.Value)
                        {
                            try { isOverdue = Convert.ToDateTime(dnaRaw).Date < DateTime.Today; }
                            catch { /* unparseable date — treat as not overdue */ }
                        }
                        item.isOverdue = isOverdue;

                        // isInactive: no update for 7+ days.
                        int inactiveDays = row["inactive_days"] != DBNull.Value
                            ? Util.GetValueOfInt(row["inactive_days"]) : 0;
                        item.isInactive = !isPast && inactiveDays >= 7;

                        items.Add(item);
                    }

                    // For open tickets, total = count of items returned
                    if (!isPast)
                        response.total = items.Count;
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetTickets", ex.Message);
                response.sqlError = ex.Message;
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §7  Orders  (paged)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a paged list of sales orders for the specified C_BPartner, with
        /// amounts converted to the client's base currency.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <param name="pageOffset">Zero-based row offset.</param>
        /// <param name="pageSize">Number of rows per page.</param>
        /// <returns>
        /// Dynamic object with <c>items</c> and base-currency metadata.
        /// Each item exposes: <c>id</c>, <c>orderNo</c>, <c>orderDate</c>,
        /// <c>items</c> (description), <c>statusCode</c>, <c>amount</c>.
        /// </returns>
        public dynamic GetOrders(Ctx ctx, int bPartnerId, int pageOffset, int pageSize)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            response.total = 0;

            int adClientId = ctx.GetAD_Client_ID();

            try
            {
                dynamic currMeta = GetCurrencyMeta(ctx, adClientId);
                response.baseCurrId = currMeta.baseCurrId;

                var metaDict = (IDictionary<string, object>)currMeta;
                if (metaDict.ContainsKey("currencySymbol"))
                    response.currencySymbol = currMeta.currencySymbol;
                if (metaDict.ContainsKey("currencyIso"))
                    response.currencyIso = currMeta.currencyIso;
                if (metaDict.ContainsKey("precision"))
                    response.precision = currMeta.precision;

                // Total count for pagination
                var cntSb = new StringBuilder();
                cntSb.Append("SELECT COUNT(*) FROM C_Order o");
                cntSb.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = o.AD_Client_ID)");
                cntSb.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                cntSb.Append(" WHERE o.IsActive = 'Y' AND o.IsSOTrx = 'Y' AND o.C_BPartner_ID = @bPartnerId");
                cntSb.Append("   AND COALESCE(o.IsSalesQuotation,'N') = 'N' AND COALESCE(o.IsBlanketTrx,'N') = 'N' AND COALESCE(o.IsReturnTrx,'N') = 'N'");
                string cntAccessSql = MRole.GetDefault(ctx).AddAccessSQL(cntSb.ToString(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                object cntResult = DB.ExecuteScalar(cntAccessSql, new SqlParameter[] { new SqlParameter("@bPartnerId", bPartnerId) }, null);
                response.total = cntResult != null && cntResult != DBNull.Value ? Util.GetValueOfInt(cntResult) : 0;

                var sb = new StringBuilder();
                sb.Append("SELECT o.C_Order_ID AS id,");
                sb.Append("       o.DocumentNo AS order_no,");
                sb.Append("       TO_CHAR(o.DateOrdered,'YYYY-MM-DD') AS order_date,");
                sb.Append("       o.Description AS items,");
                sb.Append("       o.DocStatus AS status_code,");
                sb.Append("       CURRENCYCONVERT(o.GrandTotal, o.C_Currency_ID, cs.C_Currency_ID, o.DateOrdered, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID) AS amount");
                sb.Append("  FROM C_Order o");
                sb.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = o.AD_Client_ID)");
                sb.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                sb.Append(" WHERE o.IsActive = 'Y' AND o.IsSOTrx = 'Y' AND o.C_BPartner_ID = @bPartnerId");
                sb.Append("   AND COALESCE(o.IsSalesQuotation,'N') = 'N' AND COALESCE(o.IsBlanketTrx,'N') = 'N' AND COALESCE(o.IsReturnTrx,'N') = 'N'");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // ORDER BY + pagination appended after AddAccessSQL (RULE 5)
                accessSql += " ORDER BY o.DateOrdered DESC OFFSET @pageOffset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@bPartnerId", bPartnerId),
                    new SqlParameter("@pageOffset", pageOffset),
                    new SqlParameter("@pageSize",   pageSize)
                };

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.id = Util.GetValueOfInt(row["id"]);
                        item.orderNo = Util.GetValueOfString(row["order_no"]);
                        item.orderDate = Util.GetValueOfString(row["order_date"]);
                        item.items = Util.GetValueOfString(row["items"]);
                        item.statusCode = Util.GetValueOfString(row["status_code"]);
                        item.amount = row["amount"] != DBNull.Value
                            ? Convert.ToDecimal(row["amount"]) : 0m;
                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetOrders", ex.Message);
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §7b  GetWindowId — window navigation helper
        // ─────────────────────────────────────────────────────────

        /// <summary>Looks up AD_Window_ID by window Name for client-side navigation.</summary>
        public dynamic GetWindowId(Ctx ctx, string windowName)
        {
            dynamic response = new ExpandoObject();
            response.windowId = 0;
            try
            {
                if (string.IsNullOrEmpty(windowName)) return response;

                var sb = new StringBuilder();
                sb.Append("SELECT w.AD_Window_ID FROM AD_Window w");
                sb.Append(" WHERE w.IsActive = 'Y'");
                sb.Append("   AND w.AD_Client_ID IN (0, @clientId)");
                sb.Append("   AND w.Name = @windowName");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "w", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                accessSql += " ORDER BY CASE WHEN w.AD_Client_ID = @clientId THEN 0 ELSE 1 END";
                accessSql += " FETCH FIRST 1 ROW ONLY";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@clientId",   ctx.GetAD_Client_ID()),
                    new SqlParameter("@windowName", Util.GetValueOfString(windowName))
                };

                object result = DB.ExecuteScalar(accessSql, sqlParams, null);
                if (result != null && result != DBNull.Value)
                    response.windowId = Util.GetValueOfInt(result);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetWindowId", ex.Message);
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §8  Invoices  (paged)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a paged list of completed/closed sales invoices for the specified
        /// C_BPartner, with amounts converted to the client's base currency.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <param name="pageOffset">Zero-based row offset.</param>
        /// <param name="pageSize">Number of rows per page.</param>
        /// <returns>
        /// Dynamic object with <c>items</c> and base-currency metadata.
        /// Each item exposes: <c>id</c>, <c>invoiceNo</c>, <c>invoiceDate</c>,
        /// <c>dueDate</c>, <c>amount</c>, <c>paid</c>, <c>payStatus</c>.
        /// </returns>
        public dynamic GetInvoices(Ctx ctx, int bPartnerId, int pageOffset, int pageSize)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            response.total = 0;

            int adClientId = ctx.GetAD_Client_ID();

            try
            {
                dynamic currMeta = GetCurrencyMeta(ctx, adClientId);
                response.baseCurrId = currMeta.baseCurrId;

                var metaDict = (IDictionary<string, object>)currMeta;
                if (metaDict.ContainsKey("currencySymbol"))
                    response.currencySymbol = currMeta.currencySymbol;
                if (metaDict.ContainsKey("currencyIso"))
                    response.currencyIso = currMeta.currencyIso;
                if (metaDict.ContainsKey("precision"))
                    response.precision = currMeta.precision;

                // Total count for pagination
                var cntSb = new StringBuilder();
                cntSb.Append("SELECT COUNT(*) FROM C_Invoice i");
                cntSb.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = i.AD_Client_ID)");
                cntSb.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                cntSb.Append(" WHERE i.IsActive = 'Y' AND i.IsSOTrx = 'Y' AND i.DocStatus IN ('CO','CL') AND i.C_BPartner_ID = @bPartnerId");
                string cntAccessSql = MRole.GetDefault(ctx).AddAccessSQL(cntSb.ToString(), "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                object cntResult = DB.ExecuteScalar(cntAccessSql, new SqlParameter[] { new SqlParameter("@bPartnerId", bPartnerId) }, null);
                response.total = cntResult != null && cntResult != DBNull.Value ? Util.GetValueOfInt(cntResult) : 0;

                var sb = new StringBuilder();
                sb.Append("SELECT i.C_Invoice_ID AS id,");
                sb.Append("       i.DocumentNo AS invoice_no,");
                sb.Append("       TO_CHAR(i.DateInvoiced,'YYYY-MM-DD') AS invoice_date,");
                sb.Append("       COALESCE(");
                sb.Append("           (SELECT TO_CHAR(MIN(ps.DueDate),'YYYY-MM-DD')");
                sb.Append("              FROM C_InvoicePaySchedule ps");
                sb.Append("             WHERE ps.C_Invoice_ID = i.C_Invoice_ID AND ps.IsActive = 'Y'),");
                sb.Append("           TO_CHAR(i.DueDate,'YYYY-MM-DD')) AS due_date,");
                sb.Append("       CURRENCYCONVERT(i.GrandTotal, i.C_Currency_ID, cs.C_Currency_ID,");
                sb.Append("           i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID) AS amount,");
                sb.Append("       COALESCE(CURRENCYCONVERT(i.PaidAmt, i.C_Currency_ID, cs.C_Currency_ID,");
                sb.Append("           i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) AS paid_amt,");
                sb.Append("       CASE WHEN i.IsPaid = 'Y' THEN 'Paid'");
                sb.Append("            WHEN COALESCE(");
                sb.Append("                     (SELECT MIN(ps.DueDate) FROM C_InvoicePaySchedule ps");
                sb.Append("                       WHERE ps.C_Invoice_ID = i.C_Invoice_ID AND ps.IsActive = 'Y'),");
                sb.Append("                     i.DueDate) < CURRENT_DATE THEN 'Overdue'");
                sb.Append("            ELSE 'Open' END AS pay_status");
                sb.Append("  FROM C_Invoice i");
                sb.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = i.AD_Client_ID)");
                sb.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                sb.Append(" WHERE i.IsActive = 'Y' AND i.IsSOTrx = 'Y' AND i.DocStatus IN ('CO','CL') AND i.C_BPartner_ID = @bPartnerId");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // ORDER BY + pagination appended after AddAccessSQL (RULE 5)
                accessSql += " ORDER BY i.DateInvoiced DESC OFFSET @pageOffset ROWS FETCH NEXT @pageSize ROWS ONLY";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@bPartnerId", bPartnerId),
                    new SqlParameter("@pageOffset", pageOffset),
                    new SqlParameter("@pageSize",   pageSize)
                };

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.id = Util.GetValueOfInt(row["id"]);
                        item.invoiceNo = Util.GetValueOfString(row["invoice_no"]);
                        item.invoiceDate = Util.GetValueOfString(row["invoice_date"]);
                        item.dueDate = Util.GetValueOfString(row["due_date"]);
                        item.amount = row["amount"] != DBNull.Value
                            ? Convert.ToDecimal(row["amount"]) : 0m;
                        item.paid = row["paid_amt"] != DBNull.Value
                            ? Convert.ToDecimal(row["paid_amt"]) : 0m;
                        item.payStatus = Util.GetValueOfString(row["pay_status"]);
                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetInvoices", ex.Message);
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §9  Projects
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active non-opportunity projects linked to the specified
        /// C_BPartner, with budgets converted to the client's base currency.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <returns>
        /// Dynamic object with <c>items</c> and base-currency metadata.
        /// Each item exposes: <c>id</c>, <c>name</c>, <c>statusCode</c>, <c>due</c>,
        /// <c>lead</c>, <c>budget</c>, <c>startDate</c>, <c>description</c>.
        /// </returns>
        public dynamic GetProjects(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();

            int adClientId = ctx.GetAD_Client_ID();

            try
            {
                dynamic currMeta = GetCurrencyMeta(ctx, adClientId);
                response.baseCurrId = currMeta.baseCurrId;

                var metaDict = (IDictionary<string, object>)currMeta;
                if (metaDict.ContainsKey("currencySymbol"))
                    response.currencySymbol = currMeta.currencySymbol;
                if (metaDict.ContainsKey("currencyIso"))
                    response.currencyIso = currMeta.currencyIso;
                if (metaDict.ContainsKey("precision"))
                    response.precision = currMeta.precision;

                var sb = new StringBuilder();
                sb.Append("SELECT p.C_Project_ID AS id,");
                sb.Append("       p.Name AS name,");
                sb.Append("       NULL AS status_code,");
                sb.Append("       TO_CHAR(p.DateFinish,'YYYY-MM-DD') AS due,");
                sb.Append("       lead.Name AS lead,");
                sb.Append("       CURRENCYCONVERT(p.PlannedAmt, p.C_Currency_ID, cs.C_Currency_ID, p.DateContract, NULL, p.AD_Client_ID, p.AD_Org_ID) AS budget,");
                sb.Append("       TO_CHAR(p.DateContract,'YYYY-MM-DD') AS start_date,");
                sb.Append("       p.Description");
                sb.Append("  FROM C_Project p");
                sb.Append("  LEFT OUTER JOIN AD_User lead ON (lead.AD_User_ID = p.SalesRep_ID)");
                sb.Append("  INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = p.AD_Client_ID)");
                sb.Append("  INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)");
                sb.Append(" WHERE p.IsActive = 'Y' AND COALESCE(p.IsOpportunity,'N') = 'N' AND p.C_BPartner_ID = @bPartnerId");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // ORDER BY appended after AddAccessSQL (RULE 5)
                accessSql += " ORDER BY p.DateContract DESC";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@bPartnerId", bPartnerId)
                };

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.id = Util.GetValueOfInt(row["id"]);
                        item.name = Util.GetValueOfString(row["name"]);
                        item.statusCode = Util.GetValueOfString(row["status_code"]);
                        item.due = Util.GetValueOfString(row["due"]);
                        item.lead = Util.GetValueOfString(row["lead"]);
                        item.budget = row["budget"] != DBNull.Value
                            ? Convert.ToDecimal(row["budget"]) : 0m;
                        item.startDate = Util.GetValueOfString(row["start_date"]);
                        item.description = Util.GetValueOfString(row["Description"]);
                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetProjects", ex.Message);
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §10  Timeline  (paged, multi-source)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a paged engagement timeline aggregated from all activity sources:
        /// Calls (VA048_CallDetails), Appointments/Tasks (AppointmentsInfo),
        /// Mails/Letters (MailAttachment1), and Chat (CM_Chat + CM_ChatEntry).
        /// Each source is fetched in its own try/catch so a missing table never
        /// breaks the overall response. Results are merged, sorted newest-first,
        /// then paginated in memory.
        /// </summary>
        public dynamic GetTimeline(Ctx ctx, int bPartnerId, int pageOffset, int pageSize)
        {
            dynamic response = new ExpandoObject();
            response.isExtension = true;

            var allItems = new List<dynamic>();
            int callCnt = 0, meetingCnt = 0, taskCnt = 0,
                emailCnt = 0, letterCnt = 0, chatCnt = 0, noteCnt = 0;

            // AppointmentsInfo and MailAttachment1 link to parent entities via
            // Record_ID + AD_Table_ID (polymorphic), not via a direct C_BPartner_ID column.
            int bPartnerTableId = GetTableId("C_BPartner");

            // ── Helper: add a single timeline item ────────────────
            Action<DataRow, string> addItem = (row, touchType) =>
            {
                dynamic item = new ExpandoObject();
                item.touchType = touchType;
                item.whenTs = Util.GetValueOfString(row["when_ts"]);
                item.title = Util.GetValueOfString(row["title"]);
                item.who = Util.GetValueOfString(row["who"]);
                allItems.Add(item);
            };

            // ── 1. Calls — VA048_CallDetails (skipped when VA048 not installed) ──
            if (Env.IsModuleInstalled("VA048_"))
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.Append("SELECT TO_CHAR(cd.Created,'YYYY-MM-DD HH24:MI') AS when_ts,");
                    sb.Append("       COALESCE(NULLIF(cd.VA048_CallNotes, N''), cd.VA048_To, '') AS title,");
                    sb.Append("       u.Name AS who");
                    sb.Append("  FROM VA048_CallDetails cd");
                    sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = cd.CreatedBy)");
                    sb.Append(" WHERE cd.IsActive = 'Y'");
                    sb.Append("   AND cd.Record_ID = @bpId");
                    if (bPartnerTableId > 0)
                        sb.Append("   AND cd.AD_Table_ID = " + bPartnerTableId);

                    string sql = MRole.GetDefault(ctx).AddAccessSQL(
                        sb.ToString(), "cd", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    DataSet ds = DB.ExecuteDataset(sql,
                        new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) }, null);
                    if (ds != null && ds.Tables.Count > 0)
                    {
                        callCnt = ds.Tables[0].Rows.Count;
                        foreach (DataRow row in ds.Tables[0].Rows) addItem(row, "CALL");
                    }
                }
                catch (Exception ex) { _log.SaveError("GetTimeline.Calls", ex.Message); }
            }

            // ── 2. Appointments (ISTASK='N') — AppointmentsInfo ───
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT TO_CHAR(ai.StartDate,'YYYY-MM-DD HH24:MI') AS when_ts,");
                sb.Append("       ai.Subject AS title,");
                sb.Append("       u.Name AS who");
                sb.Append("  FROM AppointmentsInfo ai");
                sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ai.CreatedBy)");
                sb.Append(" WHERE ai.IsActive = 'Y' AND ai.Record_ID = @bpId AND ai.AD_Table_ID = " + bPartnerTableId);
                sb.Append("   AND ai.ISTASK = 'N'");

                string sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "ai", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    meetingCnt = ds.Tables[0].Rows.Count;
                    foreach (DataRow row in ds.Tables[0].Rows) addItem(row, "MEETING");
                }
            }
            catch (Exception ex) { _log.SaveError("GetTimeline.Appointments", ex.Message); }

            // ── 3. Tasks (ISTASK='Y') — AppointmentsInfo ──────────
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT TO_CHAR(ai.StartDate,'YYYY-MM-DD HH24:MI') AS when_ts,");
                sb.Append("       ai.Subject AS title,");
                sb.Append("       u.Name AS who");
                sb.Append("  FROM AppointmentsInfo ai");
                sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ai.CreatedBy)");
                sb.Append(" WHERE ai.IsActive = 'Y' AND ai.Record_ID = @bpId AND ai.AD_Table_ID = " + bPartnerTableId);
                sb.Append("   AND ai.ISTASK = 'Y'");

                string sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "ai", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    taskCnt = ds.Tables[0].Rows.Count;
                    foreach (DataRow row in ds.Tables[0].Rows) addItem(row, "TASK");
                }
            }
            catch (Exception ex) { _log.SaveError("GetTimeline.Tasks", ex.Message); }

            // ── 4. Notes — AD_Note ────────────────────────────────
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT TO_CHAR(n.Updated,'YYYY-MM-DD HH24:MI') AS when_ts,");
                sb.Append("       COALESCE(n.TextMsg, n.Description, '') AS title,");
                sb.Append("       u.Name AS who");
                sb.Append("  FROM AD_Note n");
                sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = n.CreatedBy)");
                sb.Append(" WHERE n.IsActive = 'Y' AND n.Record_ID = @bpId AND n.AD_Table_ID = " + bPartnerTableId);

                string sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "n", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    noteCnt = ds.Tables[0].Rows.Count;
                    foreach (DataRow row in ds.Tables[0].Rows) addItem(row, "NOTE");
                }
            }
            catch (Exception ex) { _log.SaveError("GetTimeline.Notes", ex.Message); }

            // ── 5. Emails & Letters — MailAttachment1 ────────────
            // AttachmentType 'M' = email sent/received, 'I' = inbound letter.
            // Timestamp: DateMailReceived for letters, Created for emails (mirrors LatestUpdates block 6).
            // Title: ma.Title (not ma.Subject) per platform convention.
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT CASE WHEN ma.AttachmentType = 'I'");
                sb.Append("            THEN TO_CHAR(ma.DateMailReceived,'YYYY-MM-DD HH24:MI')");
                sb.Append("            ELSE TO_CHAR(ma.Created,'YYYY-MM-DD HH24:MI') END AS when_ts,");
                sb.Append("       ma.Title AS title,");
                sb.Append("       u.Name AS who,");
                sb.Append("       ma.AttachmentType AS attachment_type");
                sb.Append("  FROM MailAttachment1 ma");
                sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy)");
                sb.Append(" WHERE ma.IsActive = 'Y' AND ma.Record_ID = @bpId AND ma.AD_Table_ID = " + bPartnerTableId);
                sb.Append("   AND ma.AttachmentType IN ('M', 'I')");

                string sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "ma", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        string aType = Util.GetValueOfString(row["attachment_type"]);
                        if (aType == "I") { letterCnt++; addItem(row, "LETTER"); }
                        else { emailCnt++; addItem(row, "EMAIL"); }
                    }
                }
            }
            catch (Exception ex) { _log.SaveError("GetTimeline.Emails", ex.Message); }

            // ── 6. Chat — CM_Chat + CM_ChatEntry ─────────────────
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT TO_CHAR(ce.Created,'YYYY-MM-DD HH24:MI') AS when_ts,");
                sb.Append("       ce.CharacterData AS title,");
                sb.Append("       u.Name AS who");
                sb.Append("  FROM CM_Chat c");
                sb.Append("  INNER JOIN CM_ChatEntry ce ON (ce.CM_Chat_ID = c.CM_Chat_ID AND ce.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN AD_User u  ON (u.AD_User_ID  = ce.CreatedBy)");
                sb.Append(" WHERE c.IsActive = 'Y' AND c.Record_ID = @bpId AND c.AD_Table_ID = " + bPartnerTableId);

                string sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    chatCnt = ds.Tables[0].Rows.Count;
                    foreach (DataRow row in ds.Tables[0].Rows) addItem(row, "CHAT");
                }
            }
            catch (Exception ex) { _log.SaveError("GetTimeline.Chat", ex.Message); }

            // ── Sort newest-first (YYYY-MM-DD HH24:MI is lexicographically ordered) ─
            allItems.Sort((a, b) =>
            {
                var da = (IDictionary<string, object>)a;
                var db = (IDictionary<string, object>)b;
                string ta = da.ContainsKey("whenTs") ? (da["whenTs"] ?? "").ToString() : "";
                string tb = db.ContainsKey("whenTs") ? (db["whenTs"] ?? "").ToString() : "";
                return string.Compare(tb, ta, StringComparison.Ordinal);
            });

            // ── Build per-type counts for channel strip ────────────
            dynamic counts = new ExpandoObject();
            counts.call = callCnt;
            counts.meeting = meetingCnt;
            counts.task = taskCnt;
            counts.email = emailCnt;
            counts.letter = letterCnt;
            counts.chat = chatCnt;
            counts.note = noteCnt;

            response.counts = counts;
            response.total = allItems.Count;

            // ── Paginate in memory ────────────────────────────────
            int start = Math.Min(pageOffset, allItems.Count);
            int take = Math.Min(pageSize, allItems.Count - start);
            response.items = take > 0
                ? allItems.GetRange(start, take)
                : new List<dynamic>();

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §11  Notes  (extension — graceful degradation)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns notes for the specified C_BPartner from the VA_AccountNote extension
        /// table.  If the table does not yet exist the method catches the exception and
        /// returns an empty list with <c>available = false</c> so the UI can degrade
        /// gracefully without surfacing a hard error.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the customer account.</param>
        /// <returns>
        /// Dynamic object with <c>items</c>, <c>isExtension</c> = true, and
        /// <c>available</c> (bool — false when the extension table is absent).
        /// Each item exposes: <c>id</c>, <c>noteText</c>, <c>created</c>,
        /// <c>isPinned</c>, <c>author</c>.
        /// </returns>
        public dynamic GetNotes(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            response.isExtension = false;
            response.available = true;

            try
            {
                int bpTableId = MTable.Get_Table_ID("C_BPartner");

                var sb = new StringBuilder();
                sb.Append("SELECT e.CM_ChatEntry_ID AS id,");
                sb.Append("       SUBSTR(e.CharacterData, 1, 4000) AS note_text,");
                sb.Append("       COALESCE(e.Subject, N'') AS subject,");
                sb.Append("       au.Name AS author,");
                sb.Append("       TO_CHAR(e.Created,'YYYY-MM-DD HH24:MI') AS created,");
                sb.Append("       e.CM_ChatEntryParent_ID AS parent_id,");
                sb.Append("       e.ConfidentialType AS confidentiality");
                sb.Append("  FROM CM_ChatEntry e");
                sb.Append("  INNER JOIN CM_Chat c ON (c.CM_Chat_ID = e.CM_Chat_ID AND c.IsActive = 'Y'");
                if (bpTableId > 0)
                    sb.Append("    AND c.AD_Table_ID = " + bpTableId);
                sb.Append("    AND c.Record_ID = @bPartnerId)");
                sb.Append("  LEFT OUTER JOIN AD_User au ON (au.AD_User_ID = COALESCE(e.AD_User_ID, e.CreatedBy) AND au.IsActive = 'Y')");
                sb.Append(" WHERE e.IsActive = 'Y'");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "e", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // ORDER BY after AddAccessSQL (RULE 5)
                accessSql += " ORDER BY e.Created DESC";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@bPartnerId", bPartnerId)
                };

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.id              = Util.GetValueOfInt(row["id"]);
                        item.noteText        = Util.GetValueOfString(row["note_text"]);
                        item.subject         = Util.GetValueOfString(row["subject"]);
                        item.author          = Util.GetValueOfString(row["author"]);
                        item.created         = Util.GetValueOfString(row["created"]);
                        item.isPinned        = false;
                        item.parentId        = Util.GetValueOfInt(row["parent_id"]);
                        item.confidentiality = Util.GetValueOfString(row["confidentiality"]);
                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetNotes", ex.Message);
                response.sqlError = ex.Message;
                response.available = false;
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §12  Post Meeting
        // ─────────────────────────────────────────────────────────
        public dynamic PostMeeting(Ctx ctx, int bPartnerId, string title, string startDateTime, string endDateTime, string location, string description)
        {
            dynamic response = new ExpandoObject();
            response.success = false;

            try
            {
                int bpTableId = MTable.Get_Table_ID("C_BPartner");

                DateTime startDate = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(startDateTime))
                    DateTime.TryParse(startDateTime, out startDate);

                DateTime endDate = startDate.AddMinutes(30);
                if (!string.IsNullOrWhiteSpace(endDateTime))
                    DateTime.TryParse(endDateTime, out endDate);

                MAppointmentsInfo appt = new MAppointmentsInfo(ctx, 0, null);
                appt.SetIsTask(false);
                appt.SetSubject(title);
                if (!string.IsNullOrWhiteSpace(location))
                    appt.SetLocation(location);
                appt.SetStartDate(startDate);
                appt.SetEndDate(endDate);
                if (!string.IsNullOrWhiteSpace(description))
                    appt.SetDescription(description);
                appt.SetAD_User_ID(ctx.GetAD_User_ID());
                if (bpTableId > 0)
                    appt.SetAD_Table_ID(bpTableId);
                appt.SetRecord_ID(bPartnerId);

                response.success = appt.Save();
                if (!(bool)response.success)
                {
                    ValueNamePair ppE = VLogger.RetrieveError();
                    response.message = ppE != null ? ppE.GetName() : "Save failed";
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.PostMeeting", ex.Message);
                response.message = ex.Message;
            }

            return response;
        }

        public dynamic PostNote(Ctx ctx, int bPartnerId, string noteText)
        {
            dynamic response = new ExpandoObject();
            response.success = false;

            try
            {
                int bpTableId = MTable.Get_Table_ID("C_BPartner");
                int clientId  = ctx.GetAD_Client_ID();
                int orgId     = ctx.GetAD_Org_ID();
                int userId    = ctx.GetAD_User_ID();

                // 1. Find existing CM_Chat for this C_BPartner
                int chatId = 0;
                string findChatSql =
                    "SELECT CM_Chat_ID FROM CM_Chat " +
                    "WHERE AD_Table_ID = @tableId AND Record_ID = @recordId " +
                    "  AND AD_Client_ID = @clientId AND IsActive = 'Y'";
                var findParams = new SqlParameter[]
                {
                    new SqlParameter("@tableId",  bpTableId),
                    new SqlParameter("@recordId", bPartnerId),
                    new SqlParameter("@clientId", clientId)
                };
                object existingId = DB.ExecuteScalar(findChatSql, findParams, null);
                if (existingId != null && existingId != DBNull.Value)
                    chatId = Util.GetValueOfInt(existingId);

                // 2. Create CM_Chat if none exists
                if (chatId <= 0)
                {
                    chatId = DB.GetNextID(ctx, "CM_Chat", null);
                    if (chatId <= 0)
                    {
                        response.message = "Could not obtain sequence ID for CM_Chat";
                        return response;
                    }

                    string createChatSql =
                        "INSERT INTO CM_Chat " +
                        "(CM_Chat_ID, AD_Client_ID, AD_Org_ID, IsActive, Created, CreatedBy, Updated, UpdatedBy, " +
                        " AD_Table_ID, Record_ID, Description, ConfidentialType, ModerationType) " +
                        "VALUES " +
                        "(@chatId, @clientId, @orgId, 'Y', CURRENT_TIMESTAMP, @createdBy, CURRENT_TIMESTAMP, @updatedBy, " +
                        " @tableId, @recordId, @description, 'A', 'A')";

                    var createChatParams = new SqlParameter[]
                    {
                        new SqlParameter("@chatId",      chatId),
                        new SqlParameter("@clientId",    clientId),
                        new SqlParameter("@orgId",       orgId),
                        new SqlParameter("@createdBy",   userId),
                        new SqlParameter("@updatedBy",   userId),
                        new SqlParameter("@tableId",     bpTableId),
                        new SqlParameter("@recordId",    bPartnerId),
                        new SqlParameter("@description", "C_BPartner Notes")
                    };

                    int chatRows = DB.ExecuteQuery(createChatSql, createChatParams, null);
                    if (chatRows <= 0)
                    {
                        response.message = "Failed to create CM_Chat record";
                        return response;
                    }
                }

                // 3. Insert CM_ChatEntry linked to the chat
                int entryId = DB.GetNextID(ctx, "CM_ChatEntry", null);
                if (entryId <= 0)
                {
                    response.message = "Could not obtain sequence ID for CM_ChatEntry";
                    return response;
                }

                string insertEntrySql =
                    "INSERT INTO CM_ChatEntry " +
                    "(CM_ChatEntry_ID, AD_Client_ID, AD_Org_ID, IsActive, Created, CreatedBy, Updated, UpdatedBy, " +
                    " CM_Chat_ID, AD_User_ID, CharacterData, ConfidentialType, ChatEntryType) " +
                    "VALUES " +
                    "(@entryId, @clientId, @orgId, 'Y', CURRENT_TIMESTAMP, @createdBy, CURRENT_TIMESTAMP, @updatedBy, " +
                    " @chatId, @adUserId, @charData, 'A', 'N')";

                var entryParams = new SqlParameter[]
                {
                    new SqlParameter("@entryId",   entryId),
                    new SqlParameter("@clientId",  clientId),
                    new SqlParameter("@orgId",     orgId),
                    new SqlParameter("@createdBy", userId),
                    new SqlParameter("@updatedBy", userId),
                    new SqlParameter("@chatId",    chatId),
                    new SqlParameter("@adUserId",  userId),
                    new SqlParameter("@charData",  noteText)
                };

                int entryRows = DB.ExecuteQuery(insertEntrySql, entryParams, null);
                response.success = (entryRows == 1);
                if (!response.success)
                    response.message = "Insert into CM_ChatEntry returned 0 rows";
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.PostNote", ex.Message);
                response.message = ex.Message;
            }

            return response;
        }

        // ── Metadata helpers ──────────────────────────────────────────────────────

        private int GetTableId(string tableName)
        {
            try
            {
                string sql = "SELECT AD_Table_ID FROM AD_Table " +
                             "WHERE TableName = @tableName AND IsActive = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql,
                    new[] { new SqlParameter("@tableName", tableName) }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Table_ID"]);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetTableId(" + tableName + ")", ex.Message);
            }
            return 0;
        }

        /// <summary>
        /// Returns a Value → display-name dictionary for the reference list attached
        /// to <paramref name="columnName"/> on <paramref name="tableName"/>,
        /// resolved in the session language with fallback to the base name / value.
        /// </summary>
        private Dictionary<string, string> GetRefListNames(Ctx ctx, string columnName, string tableName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string lang = ctx.GetAD_Language();
                var sb = new StringBuilder();
                sb.Append("SELECT rl.Value,");
                sb.Append("       COALESCE(rlt.Name, rl.Name, rl.Value) AS display_name");
                sb.Append("  FROM AD_Column c");
                sb.Append("  INNER JOIN AD_Table t  ON (t.AD_Table_ID  = c.AD_Table_ID");
                sb.Append("                             AND t.IsActive  = 'Y')");
                sb.Append("  INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID = c.AD_Reference_Value_ID");
                sb.Append("                                AND rl.IsActive = 'Y')");
                sb.Append("  LEFT JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID");
                sb.Append("                                    AND rlt.AD_Language = @lang");
                sb.Append("                                    AND rlt.IsActive    = 'Y')");
                sb.Append(" WHERE c.ColumnName = @columnName AND c.IsActive = 'Y'");
                sb.Append("   AND t.TableName  = @tableName");

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@lang",       lang),
                    new SqlParameter("@columnName", columnName),
                    new SqlParameter("@tableName",  tableName)
                };

                DataSet ds = DB.ExecuteDataset(sb.ToString(), sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        string val  = Util.GetValueOfString(row["Value"]);
                        string name = Util.GetValueOfString(row["display_name"]);
                        if (!string.IsNullOrEmpty(val) && !result.ContainsKey(val))
                            result[val] = name;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetRefListNames", ex.Message);
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────
        // GetWhatsAppChat — most-recent WSP chat topic + messages for account
        // ─────────────────────────────────────────────────────────

        public dynamic GetWhatsAppChat(Ctx ctx, int bPartnerId, int topicId = 0)
        {
            dynamic response = new ExpandoObject();
            response.topic    = null;
            response.messages = new List<dynamic>();

            if (!Env.IsModuleInstalled("WSP_"))
                return response;

            int bpTableId = MTable.Get_Table_ID("C_BPartner");
            if (bpTableId <= 0)
                return response;

            int oppTableId = MTable.Get_Table_ID("VAS_Opportunity");

            try
            {
                // ── Most recent chat topic linked to this BPartner (direct or via its opportunities) ──
                var sbTopic = new StringBuilder();
                sbTopic.Append("SELECT ct.WSP_SMChatTopic_ID AS topic_id,");
                sbTopic.Append("       COALESCE(ci.Name, N'') AS contact_name,");
                sbTopic.Append("       TO_CHAR(ct.Created, 'YYYY-MM-DD HH24:MI') AS chat_date");
                sbTopic.Append("  FROM WSP_SMChatTopic ct");
                sbTopic.Append("  LEFT OUTER JOIN WSP_SMChatIdentifier ci");
                sbTopic.Append("       ON (ci.WSP_SMChatIdentifier_ID = ct.WSP_SMChatIdentifier_ID");
                sbTopic.Append("           AND ci.IsActive = 'Y')");
                sbTopic.Append(" WHERE ct.IsActive = 'Y'");
                if (topicId > 0)
                {
                    sbTopic.Append("   AND ct.WSP_SMChatTopic_ID = @topicId");
                }
                else
                {
                    sbTopic.Append("   AND (");
                    sbTopic.Append("       (ct.AD_Table_ID = @bpTableId AND ct.Record_ID = @bpId)");
                    //if (oppTableId > 0)
                    //    sbTopic.Append("       OR (ct.AD_Table_ID = @oppTableId AND ct.Record_ID IN (" +
                    //                   "SELECT o.VAS_Opportunity_ID FROM VAS_Opportunity o " +
                    //                   "WHERE o.C_BPartner_ID = @bpId2 AND o.IsActive = 'Y'))");
                    sbTopic.Append("   )");
                }

                string topicBaseSql   = sbTopic.ToString();
                string topicAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    topicBaseSql, "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                topicAccessSql += " ORDER BY ct.Created DESC FETCH FIRST 1 ROWS ONLY";

                var topicParamList = new List<SqlParameter>();
                if (topicId > 0)
                {
                    topicParamList.Add(new SqlParameter("@topicId",   topicId));
                }
                else
                {
                    topicParamList.Add(new SqlParameter("@bpTableId", bpTableId));
                    topicParamList.Add(new SqlParameter("@bpId",      bPartnerId));
                    //if (oppTableId > 0)
                    //{
                    //    topicParamList.Add(new SqlParameter("@oppTableId", oppTableId));
                    //    topicParamList.Add(new SqlParameter("@bpId2",      bPartnerId));
                    //}
                }

                DataSet topicDs = DB.ExecuteDataset(topicAccessSql, topicParamList.ToArray(), null);
                if (topicDs == null || topicDs.Tables.Count == 0 || topicDs.Tables[0].Rows.Count == 0)
                    return response;

                DataRow topicRow        = topicDs.Tables[0].Rows[0];
                int     resolvedTopicId = Util.GetValueOfInt(topicRow["topic_id"]);

                dynamic topic     = new ExpandoObject();
                topic.topicId     = resolvedTopicId;
                topic.contactName = Util.GetValueOfString(topicRow["contact_name"]);
                topic.chatDate    = Util.GetValueOfString(topicRow["chat_date"]);
                response.topic    = topic;

                if (resolvedTopicId <= 0)
                    return response;

                // ── Messages for this topic ──
                var sbMsg = new StringBuilder();
                sbMsg.Append("SELECT m.WSP_IsSender AS is_sender,");
                sbMsg.Append("       COALESCE(m.WSP_TextMsg, TO_CLOB('')) AS text_msg,");
                sbMsg.Append("       TO_CHAR(m.Created, 'YYYY-MM-DD HH24:MI') AS msg_date");
                sbMsg.Append("  FROM WSP_SMChatMessage m");
                sbMsg.Append(" WHERE m.IsActive = 'Y'");
                sbMsg.Append("   AND m.WSP_SMChatTopic_ID = @resolvedTopicId");

                string msgBaseSql   = sbMsg.ToString();
                string msgAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    msgBaseSql, "m", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                msgAccessSql += " ORDER BY m.Created ASC";

                DataSet msgDs = DB.ExecuteDataset(msgAccessSql,
                    new SqlParameter[] { new SqlParameter("@resolvedTopicId", resolvedTopicId) }, null);
                if (msgDs != null && msgDs.Tables.Count > 0)
                {
                    foreach (DataRow row in msgDs.Tables[0].Rows)
                    {
                        dynamic m  = new ExpandoObject();
                        m.isSender = Util.GetValueOfString(row["is_sender"]);
                        m.textMsg  = Util.GetValueOfString(row["text_msg"]);
                        m.msgDate  = Util.GetValueOfString(row["msg_date"]);
                        ((List<dynamic>)response.messages).Add(m);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105_AccountRightPanelModel.GetWhatsAppChat", ex.Message);
                response.error = ex.Message;
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // GetNoteDetail
        // ─────────────────────────────────────────────────────────

        public dynamic GetNoteDetail(Ctx ctx, int noteId)
        {
            dynamic response = new ExpandoObject();
            response.id     = noteId;
            response.title  = "";
            response.body   = "";
            response.whenTs = "";
            response.who    = "";
            try
            {
                if (noteId <= 0) { response.id = 0; return response; }

                var sb = new StringBuilder();
                sb.Append("SELECT e.CM_ChatEntry_ID AS Id,");
                sb.Append("       e.Subject AS Title,");
                sb.Append("       SUBSTR(e.CharacterData, 1, 4000) AS Body,");
                sb.Append("       TO_CHAR(e.Created, 'YYYY-MM-DD HH24:MI') AS WhenTs,");
                sb.Append("       au.Name AS Who");
                sb.Append("  FROM CM_ChatEntry e");
                sb.Append("  LEFT OUTER JOIN AD_User au ON (au.AD_User_ID = COALESCE(e.AD_User_ID, e.CreatedBy) AND au.IsActive = 'Y')");
                sb.Append(" WHERE e.IsActive = 'Y'");
                sb.Append("   AND e.CM_ChatEntry_ID = @noteId");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sb.ToString(), "e", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(accessSql, new SqlParameter[] { new SqlParameter("@noteId", noteId) }, null);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow row = ds.Tables[0].Rows[0];
                    response.title  = Util.GetValueOfString(row["Title"]);
                    response.body   = Util.GetValueOfString(row["Body"]);
                    response.whenTs = Util.GetValueOfString(row["WhenTs"]);
                    response.who    = Util.GetValueOfString(row["Who"]);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105.GetNoteDetail", ex.Message);
                response.error = ex.Message;
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // GetEmailDetail
        // ─────────────────────────────────────────────────────────

        public dynamic GetEmailDetail(Ctx ctx, int emailId)
        {
            dynamic response = new ExpandoObject();
            response.id        = emailId;
            response.subject   = "";
            response.body      = "";
            response.whenTs    = "";
            response.direction = "";
            response.fromEmail = "";
            response.toEmail   = "";
            response.who       = "";
            try
            {
                if (emailId <= 0) { response.id = 0; return response; }

                var sb = new StringBuilder();
                sb.Append("SELECT ma.MailAttachment1_ID AS Id,");
                sb.Append("       ma.Title AS Subject,");
                sb.Append("       SUBSTR(ma.TextMsg, 1, 4000) AS Body,");
                sb.Append("       CASE WHEN ma.AttachmentType = 'I'");
                sb.Append("            THEN TO_CHAR(ma.DateMailReceived, 'YYYY-MM-DD HH24:MI')");
                sb.Append("            ELSE TO_CHAR(ma.Created, 'YYYY-MM-DD HH24:MI') END AS WhenTs,");
                sb.Append("       CASE WHEN ma.AttachmentType = 'I' THEN 'in' ELSE 'out' END AS Direction,");
                sb.Append("       ma.MailAddressFrom AS FromEmail,");
                sb.Append("       ma.MailAddress AS ToEmail,");
                sb.Append("       u.Name AS Who");
                sb.Append("  FROM MailAttachment1 ma");
                sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy AND u.IsActive = 'Y')");
                sb.Append(" WHERE ma.IsActive = 'Y'");
                sb.Append("   AND ma.MailAttachment1_ID = @emailId");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sb.ToString(), "ma", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(accessSql, new SqlParameter[] { new SqlParameter("@emailId", emailId) }, null);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow row = ds.Tables[0].Rows[0];
                    response.subject   = Util.GetValueOfString(row["Subject"]);
                    response.body      = Util.GetValueOfString(row["Body"]);
                    response.whenTs    = Util.GetValueOfString(row["WhenTs"]);
                    response.direction = Util.GetValueOfString(row["Direction"]);
                    response.fromEmail = Util.GetValueOfString(row["FromEmail"]);
                    response.toEmail   = Util.GetValueOfString(row["ToEmail"]);
                    response.who       = Util.GetValueOfString(row["Who"]);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105.GetEmailDetail", ex.Message);
                response.error = ex.Message;
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // GetMeetingDetail
        // ─────────────────────────────────────────────────────────

        public dynamic GetMeetingDetail(Ctx ctx, int meetingId)
        {
            dynamic response = new ExpandoObject();
            response.id           = meetingId;
            response.subject      = "";
            response.startDate    = "";
            response.endDate      = "";
            response.location     = "";
            response.meetingUrl   = "";
            response.comments     = "";
            response.transcript   = "";
            response.attendees    = "";
            response.durationMins = 0;
            try
            {
                if (meetingId <= 0) return response;

                var sb = new StringBuilder();
                sb.Append("SELECT a.AppointmentsInfo_ID AS Id,");
                sb.Append("       a.Subject AS Subject,");
                sb.Append("       TO_CHAR(a.StartDate,'YYYY-MM-DD HH24:MI') AS StartDate,");
                sb.Append("       TO_CHAR(a.EndDate,'YYYY-MM-DD HH24:MI') AS EndDate,");
                sb.Append("       a.Location AS Location,");
                sb.Append("       a.MeetingUrl AS MeetingUrl,");
                sb.Append("       SUBSTR(a.Comments, 1, 4000) AS Comments,");
                sb.Append("       COALESCE(SUBSTR(a.AttendeeInfo, 1, 4000), CAST(a.AD_User_ID AS VARCHAR)) AS AttendeeInfo,");
                sb.Append("       SUBSTR(atr.Transcript, 1, 4000) AS Transcript");
                sb.Append("  FROM AppointmentsInfo a");
                sb.Append("  LEFT OUTER JOIN AppointmentTranscript atr ON (atr.AppointmentsInfo_ID = a.AppointmentsInfo_ID)");
                sb.Append(" WHERE a.IsActive = 'Y'");
                sb.Append("   AND a.AppointmentsInfo_ID = @meetingId");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sb.ToString(), "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(accessSql, new SqlParameter[] { new SqlParameter("@meetingId", meetingId) }, null);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    var row = ds.Tables[0].Rows[0];
                    response.subject    = Util.GetValueOfString(row["Subject"]);
                    response.startDate  = Util.GetValueOfString(row["StartDate"]);
                    response.endDate    = Util.GetValueOfString(row["EndDate"]);
                    response.location   = Util.GetValueOfString(row["Location"]);
                    response.meetingUrl = Util.GetValueOfString(row["MeetingUrl"]);
                    response.comments   = Util.GetValueOfString(row["Comments"]);
                    response.transcript = Util.GetValueOfString(row["Transcript"]);

                    try
                    {
                        var startStr = Util.GetValueOfString(row["StartDate"]);
                        var endStr   = Util.GetValueOfString(row["EndDate"]);
                        if (!string.IsNullOrEmpty(startStr) && !string.IsNullOrEmpty(endStr))
                        {
                            DateTime dtS = DateTime.ParseExact(startStr, "yyyy-MM-dd HH:mm", null);
                            DateTime dtE = DateTime.ParseExact(endStr,   "yyyy-MM-dd HH:mm", null);
                            response.durationMins = (int)(dtE - dtS).TotalMinutes;
                        }
                    }
                    catch { }

                    var attendeeRaw = Util.GetValueOfString(row["AttendeeInfo"]);
                    if (!string.IsNullOrEmpty(attendeeRaw))
                    {
                        // AttendeeInfo uses semicolons or commas as delimiters
                        var numericIds    = new List<string>();
                        var literalNames  = new List<string>();
                        var separators    = new char[] { ';', ',' };
                        foreach (var s in attendeeRaw.Split(separators))
                        {
                            var t = s.Trim();
                            if (string.IsNullOrEmpty(t)) continue;
                            int parsed;
                            if (int.TryParse(t, out parsed))
                            {
                                if (!numericIds.Contains(t)) numericIds.Add(t);
                            }
                            else
                            {
                                // Store the raw token as a display name
                                if (!literalNames.Contains(t)) literalNames.Add(t);
                            }
                        }

                        var resolvedNames = new List<string>(literalNames);

                        // Look up names for numeric IDs from AD_User
                        if (numericIds.Count > 0)
                        {
                            var paramNames    = new List<string>();
                            var nameParamList = new List<SqlParameter>();
                            for (int idx = 0; idx < numericIds.Count; idx++)
                            {
                                paramNames.Add("@uid" + idx);
                                int uid; int.TryParse(numericIds[idx], out uid);
                                nameParamList.Add(new SqlParameter("@uid" + idx, uid));
                            }
                            string namesSql = "SELECT Name FROM AD_User WHERE IsActive = 'Y' AND AD_User_ID IN (" + string.Join(",", paramNames) + ") ORDER BY Name";
                            DataSet nameDs  = DB.ExecuteDataset(namesSql, nameParamList.ToArray(), null);
                            if (nameDs != null && nameDs.Tables.Count > 0)
                            {
                                foreach (DataRow nr in nameDs.Tables[0].Rows)
                                {
                                    var n = Util.GetValueOfString(nr["Name"]);
                                    if (!string.IsNullOrEmpty(n)) resolvedNames.Add(n);
                                }
                            }
                        }

                        if (resolvedNames.Count > 0)
                            response.attendees = string.Join(", ", resolvedNames);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105.GetMeetingDetail", ex.Message);
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // SaveMeetingComments
        // ─────────────────────────────────────────────────────────

        public dynamic SaveMeetingComments(Ctx ctx, int meetingId, string comments, string meetingUrl)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            try
            {
                if (meetingId <= 0) return response;

                var sb = new StringBuilder();
                sb.Append("UPDATE AppointmentsInfo");
                sb.Append("   SET Comments   = @comments,");
                sb.Append("       MeetingUrl = @meetingUrl,");
                sb.Append("       UpdatedBy  = @userId,");
                sb.Append("       Updated    = CURRENT_TIMESTAMP");
                sb.Append(" WHERE AppointmentsInfo_ID = @meetingId");
                sb.Append("   AND IsActive = 'Y'");

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@comments",   Util.GetValueOfString(comments)),
                    new SqlParameter("@meetingUrl", Util.GetValueOfString(meetingUrl)),
                    new SqlParameter("@userId",     ctx.GetAD_User_ID()),
                    new SqlParameter("@meetingId",  meetingId)
                };

                int rows = DB.ExecuteQuery(sb.ToString(), sqlParams, null);
                response.success = (rows >= 0);
                if (rows < 0)
                    response.error = Msg.GetMsg(ctx, "SaveError");
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_105.SaveMeetingComments", ex.Message);
                response.error = ex.Message;
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // GetEngagement — meetings + notes + emails + calls + chat for a BPartner
        // ─────────────────────────────────────────────────────────

        public dynamic GetEngagement(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            var allItems = new List<dynamic>();

            int bpTableId = MTable.Get_Table_ID("C_BPartner");

            int countMeetings = 0;
            int countNotes    = 0;
            int countEmails   = 0;
            int countCalls    = 0;
            int countChat     = 0;

            int totalMeetingMins    = 0;
            var allAttendeeIds      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalCallMins       = 0;

            // ── Step 1: MEETINGS ─────────────────────────────────────────────
            if (bpTableId > 0)
            {
                try
                {
                    var sbMt = new StringBuilder();
                    sbMt.Append("SELECT MIN(a.AppointmentsInfo_ID) AS MeetingId,");
                    sbMt.Append("       TO_CHAR(MIN(a.StartDate),'YYYY-MM-DD HH24:MI') AS when_ts,");
                    sbMt.Append("       TO_CHAR(MIN(a.EndDate),'YYYY-MM-DD HH24:MI') AS end_date,");
                    sbMt.Append("       COALESCE(MIN(a.Subject), N'') AS title,");
                    sbMt.Append("       COALESCE(MIN(a.Location), N'') AS location,");
                    sbMt.Append("       COALESCE(MIN(SUBSTR(a.Comments, 1, 200)), N'') AS preview,");
                    sbMt.Append("       CASE WHEN MIN(atr.AppointmentsInfo_ID) IS NOT NULL THEN 'Y' ELSE 'N' END AS has_transcript,");
                    sbMt.Append("       MIN(u.Name) AS who");
                    sbMt.Append("  FROM AppointmentsInfo a");
                    sbMt.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = a.CreatedBy AND u.IsActive = 'Y')");
                    sbMt.Append("  LEFT OUTER JOIN AppointmentTranscript atr ON (atr.AppointmentsInfo_ID = a.AppointmentsInfo_ID)");
                    sbMt.Append(" WHERE a.IsActive = 'Y'");
                    sbMt.Append("   AND COALESCE(a.IsTask, 'N') = 'N'");
                    sbMt.Append("   AND a.AD_Table_ID = " + bpTableId);
                    sbMt.Append("   AND a.Record_ID = @bpId");

                    string mtBaseSql = sbMt.ToString();
                    string mtAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        mtBaseSql, "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    // RULE 5: GROUP BY and ORDER BY must come after AddAccessSQL
                    mtAccessSql += " GROUP BY a.StartDate, a.Subject";
                    mtAccessSql += " ORDER BY MIN(a.StartDate) DESC";

                    var mtParams = new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) };

                    DataSet mtDs = DB.ExecuteDataset(mtAccessSql, mtParams, null);
                    if (mtDs != null && mtDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in mtDs.Tables[0].Rows)
                        {
                            dynamic item = new ExpandoObject();
                            item.touchType = "MEETING";
                            item.meetingId = Util.GetValueOfInt(row["MeetingId"]);
                            item.whenTs    = Util.GetValueOfString(row["when_ts"]);
                            item.title     = Util.GetValueOfString(row["title"]);
                            item.location  = Util.GetValueOfString(row["location"]);
                            item.preview   = Util.GetValueOfString(row["preview"]);
                            item.hasTranscript = Util.GetValueOfString(row["has_transcript"]) == "Y";
                            item.who       = Util.GetValueOfString(row["who"]);
                            item.direction = "";
                            item.durationMins = 0;
                            try
                            {
                                var startStr = Util.GetValueOfString(row["when_ts"]);
                                var endStr   = Util.GetValueOfString(row["end_date"]);
                                if (!string.IsNullOrEmpty(startStr) && !string.IsNullOrEmpty(endStr))
                                {
                                    DateTime dtS = DateTime.ParseExact(startStr, "yyyy-MM-dd HH:mm", null);
                                    DateTime dtE = DateTime.ParseExact(endStr,   "yyyy-MM-dd HH:mm", null);
                                    item.durationMins = (int)(dtE - dtS).TotalMinutes;
                                    if (item.durationMins > 0) totalMeetingMins += item.durationMins;
                                }
                            }
                            catch { }
                            allItems.Add(item);
                            countMeetings++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_105.GetEngagement.Meetings", ex.Message);
                }

                // ── Meetings aggregate: count unique attendees via separate non-aggregate query ──
                if (countMeetings > 0)
                {
                    try
                    {
                        var sbAtt = new StringBuilder();
                        sbAtt.Append("SELECT COALESCE(SUBSTR(a.AttendeeInfo, 1, 4000), N'') AS attendee_info");
                        sbAtt.Append("  FROM AppointmentsInfo a");
                        sbAtt.Append(" WHERE a.IsActive = 'Y'");
                        sbAtt.Append("   AND COALESCE(a.IsTask, 'N') = 'N'");
                        sbAtt.Append("   AND a.AD_Table_ID = " + bpTableId);
                        sbAtt.Append("   AND a.Record_ID = @bpId");
                        string attAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                            sbAtt.ToString(), "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                        DataSet attDs = DB.ExecuteDataset(attAccessSql,
                            new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) }, null);
                        if (attDs != null && attDs.Tables.Count > 0)
                        {
                            foreach (DataRow ar in attDs.Tables[0].Rows)
                            {
                                var raw = Util.GetValueOfString(ar["attendee_info"]);
                                if (!string.IsNullOrEmpty(raw))
                                {
                                    foreach (var part in raw.Split(','))
                                    {
                                        var t = part.Trim();
                                        if (!string.IsNullOrEmpty(t)) allAttendeeIds.Add(t);
                                    }
                                }
                            }
                        }
                    }
                    catch { /* AttendeeInfo read failed — attendee count stays 0 */ }
                }
            }

            // ── Step 2: NOTES (CM_Chat + CM_ChatEntry) ──────────────────────
            if (bpTableId > 0)
            {
                try
                {
                    var sbNt = new StringBuilder();
                    sbNt.Append("SELECT e.CM_ChatEntry_ID AS note_id,");
                    sbNt.Append("       TO_CHAR(e.Created,'YYYY-MM-DD HH24:MI') AS when_ts,");
                    sbNt.Append("       COALESCE(e.Subject, N'') AS title,");
                    sbNt.Append("       SUBSTR(e.CharacterData, 1, 400) AS preview,");
                    sbNt.Append("       au.Name AS who");
                    sbNt.Append("  FROM CM_ChatEntry e");
                    sbNt.Append("  INNER JOIN CM_Chat c ON (c.CM_Chat_ID = e.CM_Chat_ID AND c.IsActive = 'Y'");
                    sbNt.Append("    AND c.AD_Table_ID = " + bpTableId);
                    sbNt.Append("    AND c.Record_ID = @bpId)");
                    sbNt.Append("  LEFT OUTER JOIN AD_User au ON (au.AD_User_ID = COALESCE(e.AD_User_ID, e.CreatedBy) AND au.IsActive = 'Y')");
                    sbNt.Append(" WHERE e.IsActive = 'Y'");

                    string ntBaseSql = sbNt.ToString();
                    string ntAccessSql = MRole.GetDefault(ctx).AddAccessSQL(ntBaseSql, "e", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    ntAccessSql += " ORDER BY e.Created DESC";

                    DataSet ntDs = DB.ExecuteDataset(ntAccessSql, new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) }, null);
                    if (ntDs != null && ntDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in ntDs.Tables[0].Rows)
                        {
                            dynamic item = new ExpandoObject();
                            item.touchType = "NOTE";
                            item.noteId    = Util.GetValueOfInt(row["note_id"]);
                            item.whenTs    = Util.GetValueOfString(row["when_ts"]);
                            item.title     = Util.GetValueOfString(row["title"]);
                            item.preview   = row["preview"] == DBNull.Value ? "" : Util.GetValueOfString(row["preview"]);
                            item.who       = Util.GetValueOfString(row["who"]);
                            item.direction = "";
                            allItems.Add(item);
                            countNotes++;
                        }
                    }
                }
                catch (Exception ex) { _log.SaveError("VAS_105.GetEngagement.Notes", ex.Message); }
            }

            // ── Step 3: EMAILS ───────────────────────────────────────────────
            if (bpTableId > 0)
            {
                try
                {
                    var sbEm = new StringBuilder();
                    sbEm.Append("SELECT ma.MailAttachment1_ID AS email_id,");
                    sbEm.Append("       CASE WHEN ma.AttachmentType = 'I'");
                    sbEm.Append("            THEN TO_CHAR(ma.DateMailReceived,'YYYY-MM-DD HH24:MI')");
                    sbEm.Append("            ELSE TO_CHAR(ma.Created,'YYYY-MM-DD HH24:MI') END AS when_ts,");
                    sbEm.Append("       COALESCE(ma.Title, N'') AS title,");
                    sbEm.Append("       N'' AS preview,");
                    sbEm.Append("       u.Name AS who,");
                    sbEm.Append("       CASE WHEN ma.AttachmentType = 'I' THEN 'in' ELSE 'out' END AS direction");
                    sbEm.Append("  FROM MailAttachment1 ma");
                    sbEm.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy)");
                    sbEm.Append(" WHERE ma.IsActive = 'Y'");
                    sbEm.Append("   AND ma.AD_Table_ID = " + bpTableId);
                    sbEm.Append("   AND ma.Record_ID = @bpId");
                    sbEm.Append("   AND ma.AttachmentType IN ('M', 'I')");

                    string emBaseSql = sbEm.ToString();
                    string emAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        emBaseSql, "ma", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    var emParams = new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) };

                    DataSet emDs = DB.ExecuteDataset(emAccessSql, emParams, null);
                    if (emDs != null && emDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in emDs.Tables[0].Rows)
                        {
                            dynamic item = new ExpandoObject();
                            item.touchType = "EMAIL";
                            item.emailId   = Util.GetValueOfInt(row["email_id"]);
                            item.whenTs    = Util.GetValueOfString(row["when_ts"]);
                            item.title     = Util.GetValueOfString(row["title"]);
                            item.preview   = "";
                            item.who       = Util.GetValueOfString(row["who"]);
                            item.direction = Util.GetValueOfString(row["direction"]);
                            allItems.Add(item);
                            countEmails++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_105.GetEngagement.Emails", ex.Message);
                }
            }

            // ── Step 4: CALLS (VA048, only when installed) ───────────────────
            if (bpTableId > 0 && Env.IsModuleInstalled("VA048_"))
            {
                try
                {
                    var sbCl = new StringBuilder();
                    sbCl.Append("SELECT TO_CHAR(cd.Created,'YYYY-MM-DD HH24:MI') AS when_ts,");
                    sbCl.Append("       COALESCE(NULLIF(cd.VA048_CallNotes, N''), cd.VA048_To, N'') AS title,");
                    sbCl.Append("       N'' AS preview,");
                    sbCl.Append("       u.Name AS who");
                    sbCl.Append("  FROM VA048_CallDetails cd");
                    sbCl.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = cd.CreatedBy)");
                    sbCl.Append(" WHERE cd.IsActive = 'Y'");
                    sbCl.Append("   AND cd.Record_ID = @bpId");
                    sbCl.Append("   AND cd.AD_Table_ID = " + bpTableId);

                    string clBaseSql = sbCl.ToString();
                    string clAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        clBaseSql, "cd", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    var clParams = new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) };

                    DataSet clDs = DB.ExecuteDataset(clAccessSql, clParams, null);
                    if (clDs != null && clDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in clDs.Tables[0].Rows)
                        {
                            dynamic item = new ExpandoObject();
                            item.touchType = "CALL";
                            item.whenTs    = Util.GetValueOfString(row["when_ts"]);
                            item.title     = Util.GetValueOfString(row["title"]);
                            item.preview   = "";
                            item.who       = Util.GetValueOfString(row["who"]);
                            item.direction = "";
                            allItems.Add(item);
                            countCalls++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_105.GetEngagement.Calls", ex.Message);
                }

                // ── Calls aggregate: total duration (safe — column may not exist) ──
                if (countCalls > 0)
                {
                    try
                    {
                        var sbCallAgg = new StringBuilder();
                        sbCallAgg.Append("SELECT COALESCE(SUM(COALESCE(cd.VA048_CallDuration, 0)), 0) AS total_mins");
                        sbCallAgg.Append("  FROM VA048_CallDetails cd");
                        sbCallAgg.Append(" WHERE cd.IsActive = 'Y'");
                        sbCallAgg.Append("   AND cd.Record_ID = @bpId");
                        sbCallAgg.Append("   AND cd.AD_Table_ID = " + bpTableId);
                        string callAggSql = MRole.GetDefault(ctx).AddAccessSQL(
                            sbCallAgg.ToString(), "cd", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                        var callAggParams = new SqlParameter[] { new SqlParameter("@bpId", bPartnerId) };
                        object callDurObj = DB.ExecuteScalar(callAggSql, callAggParams, null);
                        if (callDurObj != null && callDurObj != DBNull.Value)
                            totalCallMins = Util.GetValueOfInt(callDurObj);
                    }
                    catch { /* VA048_CallDuration may not exist — safe to ignore */ }
                }
            }

            // ── Step 5: WHATSAPP CHAT ─────────────────────────────────────────────
            if (bpTableId > 0 && Env.IsModuleInstalled("WSP_"))
            {
                try
                {
                    int vasOppTableId = MTable.Get_Table_ID("VAS_Opportunity");

                    var sbCh = new StringBuilder();
                    sbCh.Append("SELECT ct.WSP_SMChatTopic_ID AS topic_id,");
                    sbCh.Append("       COALESCE(ci.Name, N'') AS contact_name,");
                    sbCh.Append("       TO_CHAR(ct.Created, 'YYYY-MM-DD HH24:MI') AS when_ts");
                    sbCh.Append("  FROM WSP_SMChatTopic ct");
                    sbCh.Append("  LEFT OUTER JOIN WSP_SMChatIdentifier ci");
                    sbCh.Append("       ON (ci.WSP_SMChatIdentifier_ID = ct.WSP_SMChatIdentifier_ID");
                    sbCh.Append("           AND ci.IsActive = 'Y')");
                    sbCh.Append(" WHERE ct.IsActive = 'Y'");
                    sbCh.Append("   AND (");
                    sbCh.Append("       (ct.AD_Table_ID = @chBpTableId AND ct.Record_ID = @chBpId)");
                    if (vasOppTableId > 0)
                        sbCh.Append("       OR (ct.AD_Table_ID = @chOppTableId AND ct.Record_ID IN (" +
                                    "SELECT o.VAS_Opportunity_ID FROM VAS_Opportunity o " +
                                    "WHERE o.C_BPartner_ID = @chBpId2 AND o.IsActive = 'Y'))");
                    sbCh.Append("   )");

                    string chBaseSql = sbCh.ToString();
                    string chAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        chBaseSql, "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    chAccessSql += " ORDER BY ct.Created DESC";

                    var chParamList = new List<SqlParameter>
                    {
                        new SqlParameter("@chBpTableId", bpTableId),
                        new SqlParameter("@chBpId",      bPartnerId)
                    };
                    if (vasOppTableId > 0)
                    {
                        chParamList.Add(new SqlParameter("@chOppTableId", vasOppTableId));
                        chParamList.Add(new SqlParameter("@chBpId2",      bPartnerId));
                    }
                    var chParams = chParamList.ToArray();

                    DataSet chDs = DB.ExecuteDataset(chAccessSql, chParams, null);
                    if (chDs != null && chDs.Tables.Count > 0 && chDs.Tables[0].Rows.Count > 0)
                    {
                        var chTopicRows = new List<DataRow>();
                        var chTopicIds  = new List<int>();
                        foreach (DataRow chRow in chDs.Tables[0].Rows)
                        {
                            int tid = Util.GetValueOfInt(chRow["topic_id"]);
                            if (tid > 0) { chTopicRows.Add(chRow); chTopicIds.Add(tid); }
                        }

                        var lastMsgMap = new Dictionary<int, string>();
                        var senderMap  = new Dictionary<int, string>();
                        if (chTopicIds.Count > 0)
                        {
                            string idIn = string.Join(",", chTopicIds);
                            var sbMsg = new StringBuilder();
                            sbMsg.Append("SELECT m.WSP_SMChatTopic_ID AS topic_id,");
                            sbMsg.Append("       COALESCE(m.WSP_TextMsg, TO_CLOB('')) AS last_msg,");
                            sbMsg.Append("       COALESCE(m.WSP_IsSender, 'N') AS is_sender");
                            sbMsg.Append("  FROM WSP_SMChatMessage m");
                            sbMsg.Append(" WHERE m.IsActive = 'Y'");
                            sbMsg.Append("   AND m.WSP_SMChatTopic_ID IN (" + idIn + ")");
                            sbMsg.Append("   AND m.Created = (SELECT MAX(m2.Created)");
                            sbMsg.Append("                      FROM WSP_SMChatMessage m2");
                            sbMsg.Append("                     WHERE m2.WSP_SMChatTopic_ID = m.WSP_SMChatTopic_ID");
                            sbMsg.Append("                       AND m2.IsActive = 'Y')");

                            DataSet msgDs = DB.ExecuteDataset(sbMsg.ToString(), null, null);
                            if (msgDs != null && msgDs.Tables.Count > 0)
                            {
                                foreach (DataRow mRow in msgDs.Tables[0].Rows)
                                {
                                    int tid = Util.GetValueOfInt(mRow["topic_id"]);
                                    if (tid > 0 && !lastMsgMap.ContainsKey(tid))
                                    {
                                        lastMsgMap[tid] = Util.GetValueOfString(mRow["last_msg"]);
                                        senderMap[tid]  = Util.GetValueOfString(mRow["is_sender"]);
                                    }
                                }
                            }
                        }

                        foreach (DataRow chRow in chTopicRows)
                        {
                            int topicId    = Util.GetValueOfInt(chRow["topic_id"]);
                            string lastMsg = lastMsgMap.ContainsKey(topicId) ? lastMsgMap[topicId] : "";
                            string isSender = senderMap.ContainsKey(topicId) ? senderMap[topicId]  : "N";
                            dynamic chatItem = new ExpandoObject();
                            chatItem.touchType = "CHAT";
                            chatItem.topicId   = topicId;
                            chatItem.whenTs    = Util.GetValueOfString(chRow["when_ts"]);
                            chatItem.title     = "";
                            chatItem.preview   = lastMsg;
                            chatItem.who       = Util.GetValueOfString(chRow["contact_name"]);
                            chatItem.direction = (isSender == "Y") ? "out" : "in";
                            allItems.Add(chatItem);
                            countChat++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_105.GetEngagement.Chat", ex.Message);
                }
            }

            // ── Step 6: merge, sort newest-first ─────────────────────────────
            allItems.Sort((a, b) =>
            {
                string ta = (a as IDictionary<string, object>).ContainsKey("whenTs")
                    ? (string)((IDictionary<string, object>)a)["whenTs"] : "";
                string tb = (b as IDictionary<string, object>).ContainsKey("whenTs")
                    ? (string)((IDictionary<string, object>)b)["whenTs"] : "";
                return string.Compare(tb, ta, StringComparison.Ordinal);
            });

            // ── Step 7: build response ────────────────────────────────────────
            dynamic counts = new ExpandoObject();
            counts.total              = allItems.Count;
            counts.meetings           = countMeetings;
            counts.notes              = countNotes;
            counts.emails             = countEmails;
            counts.calls              = countCalls;
            counts.chat               = countChat;
            counts.totalMeetingMins   = totalMeetingMins;
            counts.meetingAttendees   = allAttendeeIds.Count;
            counts.totalCallMins      = totalCallMins;
            counts.connectedCalls     = countCalls;

            response.counts = counts;
            response.items  = allItems;

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §T1  GetPriorityList — AD_Ref_List values for PriorityKey
        // ─────────────────────────────────────────────────────────

        public dynamic GetPriorityList(Ctx ctx)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            try
            {
                string lang = ctx.GetAD_Language();
                var sb = new StringBuilder();
                sb.Append("SELECT r.Value AS val,");
                sb.Append("       COALESCE(trl.Name, r.Name) AS name");
                sb.Append("  FROM AD_Ref_List r");
                sb.Append("  INNER JOIN AD_Column col ON (col.AD_Reference_Value_ID = r.AD_Reference_ID");
                sb.Append("       AND col.ColumnName = 'PriorityKey' AND col.IsActive = 'Y')");
                sb.Append("  INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID = col.AD_Table_ID");
                sb.Append("       AND tbl.TableName = 'AppointmentsInfo' AND tbl.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN AD_Ref_List_Trl trl ON (trl.AD_Ref_List_ID = r.AD_Ref_List_ID");
                sb.Append("       AND trl.IsActive = 'Y' AND trl.AD_Language = @lang)");
                sb.Append(" WHERE r.IsActive = 'Y'");
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sb.ToString(), "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                accessSql += " ORDER BY r.Value";
                DataSet ds = DB.ExecuteDataset(accessSql, new SqlParameter[] { new SqlParameter("@lang", lang) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    var items = (List<dynamic>)response.items;
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        item.val  = Util.GetValueOfString(row["val"]);
                        item.name = Util.GetValueOfString(row["name"]);
                        items.Add(item);
                    }
                }
            }
            catch (Exception ex) { _log.SaveError("VAS_105.GetPriorityList", ex.Message); }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §T2  GetTasks
        // ─────────────────────────────────────────────────────────

        public dynamic GetTasks(Ctx ctx, int bPartnerId)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();
            response.total = 0;
            try
            {
                var priorityMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var priorityItems = (List<dynamic>)GetPriorityList(ctx).items;
                foreach (dynamic pi in priorityItems)
                {
                    string pval = Util.GetValueOfString(pi.val);
                    if (!string.IsNullOrEmpty(pval) && !priorityMap.ContainsKey(pval))
                        priorityMap[pval] = Util.GetValueOfString(pi.name);
                }

                int bpTableId = MTable.Get_Table_ID("C_BPartner");
                var sb = new StringBuilder();
                sb.Append("SELECT a.AppointmentsInfo_ID AS Id,");
                sb.Append("       a.Subject AS Title,");
                sb.Append("       a.Description AS Detail,");
                sb.Append("       a.Result AS Result,");
                sb.Append("       a.AppointmentCategory_ID AS CategoryId,");
                sb.Append("       cat.Name AS Category,");
                sb.Append("       TO_CHAR(a.EndDate,'YYYY-MM-DD') AS DueDate,");
                sb.Append("       a.PriorityKey AS PriorityCode,");
                sb.Append("       a.TaskStatus AS CompletionPct,");
                sb.Append("       a.ExecuteBy AS ExecuteByCode,");
                sb.Append("       COALESCE(SUBSTR(a.IsClosed,1,1),'N') AS IsClosed,");
                sb.Append("       u.AD_User_ID AS AssigneeId,");
                sb.Append("       u.Name AS Assignee");
                sb.Append("  FROM AppointmentsInfo a");
                sb.Append("  LEFT OUTER JOIN AppointmentCategory cat ON (cat.AppointmentCategory_ID = a.AppointmentCategory_ID)");
                sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = a.AD_User_ID AND u.IsActive = 'Y')");
                sb.Append(" WHERE a.IsActive = 'Y'");
                sb.Append("   AND COALESCE(a.IsDeleted,'N') = 'N'");
                sb.Append("   AND COALESCE(a.IsTask,'N') = 'Y'");
                sb.Append("   AND a.AD_Table_ID = @bpTableId");
                sb.Append("   AND a.Record_ID = @bpId");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sb.ToString(), "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                accessSql += " ORDER BY IsClosed ASC, PriorityCode ASC, DueDate ASC";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@bpTableId", bpTableId),
                    new SqlParameter("@bpId",      bPartnerId)
                };

                var items = (List<dynamic>)response.items;
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        dynamic item = new ExpandoObject();
                        string rawPriority = Util.GetValueOfString(row["PriorityCode"]);
                        item.id            = Util.GetValueOfInt(row["Id"]);
                        item.title         = Util.GetValueOfString(row["Title"]);
                        item.detail        = Util.GetValueOfString(row["Detail"]);
                        item.result        = Util.GetValueOfString(row["Result"]);
                        item.categoryId    = Util.GetValueOfInt(row["CategoryId"]);
                        item.category      = Util.GetValueOfString(row["Category"]);
                        item.dueDate       = Util.GetValueOfString(row["DueDate"]);
                        item.priorityCode  = rawPriority;
                        item.priorityLabel = priorityMap.ContainsKey(rawPriority) ? priorityMap[rawPriority] : rawPriority;
                        item.completionPct = Util.GetValueOfString(row["CompletionPct"]);
                        item.executeByCode = Util.GetValueOfString(row["ExecuteByCode"]);
                        item.isClosed      = Util.GetValueOfString(row["IsClosed"]) == "Y";
                        item.assigneeId    = Util.GetValueOfInt(row["AssigneeId"]);
                        item.assignee      = Util.GetValueOfString(row["Assignee"]);
                        items.Add(item);
                    }
                    response.total = items.Count;
                }
            }
            catch (Exception ex) { _log.SaveError("VAS_105.GetTasks", ex.Message); }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §T3  CompleteTask
        // ─────────────────────────────────────────────────────────

        public dynamic CompleteTask(Ctx ctx, int taskId)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            try
            {
                if (taskId <= 0) return response;
                var sb = new StringBuilder();
                sb.Append("UPDATE AppointmentsInfo");
                sb.Append("   SET IsClosed   = 'Y',");
                sb.Append("       TaskStatus = 100,");
                sb.Append("       UpdatedBy  = @userId,");
                sb.Append("       Updated    = CURRENT_TIMESTAMP");
                sb.Append(" WHERE AppointmentsInfo_ID = @taskId");
                sb.Append("   AND IsActive   = 'Y'");
                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@userId", ctx.GetAD_User_ID()),
                    new SqlParameter("@taskId", taskId)
                };
                int rows = DB.ExecuteQuery(sb.ToString(), sqlParams, null);
                response.success = (rows >= 0);
                if (rows < 0) _log.SaveError("VAS_105.CompleteTask", "DB error taskId=" + taskId);
            }
            catch (Exception ex) { _log.SaveError("VAS_105.CompleteTask", ex.Message); }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // §T4  ReopenTask
        // ─────────────────────────────────────────────────────────

        public dynamic ReopenTask(Ctx ctx, int taskId)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            try
            {
                if (taskId <= 0) return response;
                var sb = new StringBuilder();
                sb.Append("UPDATE AppointmentsInfo");
                sb.Append("   SET IsClosed  = 'N',");
                sb.Append("       UpdatedBy = @userId,");
                sb.Append("       Updated   = CURRENT_TIMESTAMP");
                sb.Append(" WHERE AppointmentsInfo_ID = @taskId");
                sb.Append("   AND IsActive  = 'Y'");
                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@userId", ctx.GetAD_User_ID()),
                    new SqlParameter("@taskId", taskId)
                };
                int rows = DB.ExecuteQuery(sb.ToString(), sqlParams, null);
                response.success = (rows >= 0);
                if (rows < 0) _log.SaveError("VAS_105.ReopenTask", "DB error taskId=" + taskId);
            }
            catch (Exception ex) { _log.SaveError("VAS_105.ReopenTask", ex.Message); }
            return response;
        }

        // ─────────────────────────────────────────────────────────
        // GetWhatsAppTopicMeta — chatId + mobile for WSP/Inbox/CreateChat
        // ─────────────────────────────────────────────────────────

        public dynamic GetWhatsAppTopicMeta(Ctx ctx, int topicId)
        {
            dynamic response = new ExpandoObject();
            response.chatId = 0;
            response.mobile = "";

            if (topicId <= 0 || !Env.IsModuleInstalled("WSP_"))
                return response;

            try
            {
                object chatIdObj = DB.ExecuteScalar(
                    "SELECT WSP_SMChat_ID FROM WSP_SMChatTopic WHERE IsActive = 'Y' AND WSP_SMChatTopic_ID = @topicId",
                    new SqlParameter[] { new SqlParameter("@topicId", topicId) }, null);
                if (chatIdObj != null && chatIdObj != DBNull.Value)
                    response.chatId = Util.GetValueOfInt(chatIdObj);
            }
            catch { /* column may differ — leave chatId = 0 */ }

            try
            {
                object mobileObj = DB.ExecuteScalar(
                    "SELECT ci.Identifier FROM WSP_SMChatIdentifier ci" +
                    " INNER JOIN WSP_SMChatTopic ct ON (ct.WSP_SMChatIdentifier_ID = ci.WSP_SMChatIdentifier_ID AND ct.IsActive = 'Y')" +
                    " WHERE ci.IsActive = 'Y' AND ct.WSP_SMChatTopic_ID = @topicId",
                    new SqlParameter[] { new SqlParameter("@topicId", topicId) }, null);
                if (mobileObj != null && mobileObj != DBNull.Value)
                    response.mobile = Util.GetValueOfString(mobileObj);
            }
            catch { /* column may differ — leave mobile = "" */ }

            return response;
        }
    }
}
