/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Customer Search widget endpoint
 * chronological  : Development
 * Created Date   : 2026-07-20
 * Created by     : VAI052
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
    /// Module Name : VAS_120_CustomerSearchWidget
    /// Purpose     : Provides secured, tenant-isolated customer suggestions to the
    ///               Customers module Customer Search widget. One deterministic
    ///               primary contact is resolved per customer (prefers a contact
    ///               that has an e-mail, then the most recently updated) so the
    ///               list needs a single query. MRole supplies tenant + record
    ///               access; per the CTE rule it is applied only to the main
    ///               physical table (C_BPartner), never to the CTE aliases or the
    ///               secondary AD_User / C_BP_Group joins.
    ///               The row quick actions post back here too: SaveAppointment and
    ///               SaveTask both write AppointmentsInfo through MAppointmentsInfo
    ///               (IsTask 'N' / 'Y' per Prompt_Instructions "Appointments &amp;
    ///               Tasks"); the third action, Log activity, reuses the shared
    ///               VAS_126_OpenTicketsWidget/SaveActivityLog endpoint so every
    ///               widget logs interactions to the one R_Request store.
    /// Chronological development:
    ///   VAI052      2026-07-20 Created
    ///   VAI052      2026-08-07 Added SaveAppointment / SaveTask for the search-row
    ///                          quick-action popups.
    ///   VAI052      2026-08-25 ORA-01008 on Oracle: the search returned nothing at all
    ///                          because both the suggest and the count statement failed
    ///                          before a row was read. The shared CTE repeats its
    ///                          placeholders (@Client_ID 2x, @Search_Exact 2x,
    ///                          @Search_Prefix 2x, @Search_Like 6x) while the parameter
    ///                          list carried ONE entry each, and it also carried an
    ///                          @Org_ID that the SQL never used. The app's Oracle layer
    ///                          never sets BindByName, so ODP.NET binds BY POSITION and
    ///                          any name occurring more often than its parameter raises
    ///                          ORA-01008 ("not all variables bound") - the same defect
    ///                          already fixed in VAS_128 / VAS_140 / VAS_099. Every
    ///                          occurrence now has its own uniquely named parameter,
    ///                          supplied in the order the placeholders appear in the
    ///                          assembled statement, and the unused @Org_ID is gone.
    /// </summary>
    public class VAS_120_CustomerSearchWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_120_CustomerSearchWidgetController).FullName);

        // Autosuggest is bounded to seven rows (design spec + Querry.md Query 1).
        private const int MaxSuggestRows = 7;

        // Server-side minimum trimmed characters before the DB is touched. The
        // client debounces and enforces the same floor; this is the backstop.
        private const int MinSearchLength = 2;

        // Display tiers. The client colours these three by name (violet / amber /
        // info); any other label falls back to its neutral tag.
        private const string TierPlatinum = "Platinum";
        private const string TierGold = "Gold";
        private const string TierSilver = "Silver";

        /// <summary>
        /// Optional OVERRIDE of the tier label per Rating code. Intentionally EMPTY,
        /// and normally stays that way: since 2026-08-07 the query resolves the tag
        /// from the tenant's own application dictionary (AD_Column('Rating') ->
        /// AD_Ref_List, translated via AD_Ref_List_Trl), so no hard-coded table is
        /// needed and no rating is ever labelled by guesswork. Populate this only to
        /// force different wording than the reference list carries, e.g. to render
        /// list entries "A"/"B"/"C" as { "A", "Platinum" }, { "B", "Gold" },
        /// { "C", "Silver" }. Entries here win over the reference-list name.
        /// </summary>
        private static readonly Dictionary<string, string> CustomerTierByRating =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Type-ahead customer search for the suggestion dropdown. Returns at most
        /// seven relevance-ranked, tenant-scoped active customers plus the total
        /// match count used by the "See all matches" affordance.
        /// </summary>
        /// <param name="q">Trimmed user search text (no % wrapping by the caller).</param>
        /// <param name="max">Requested row cap; clamped to seven.</param>
        /// <returns>JSON { Rows:[...], Total, MinLength } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult SearchCustomers(string q, int max = MaxSuggestRows)
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(SearchCustomersData(ctx, q, max));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_120_CustomerSearchWidget.SearchCustomers", ex);
                return ErrorResult(ctx);
            }
        }

        /* ── Row quick actions ────────────────────────────────────────────────
         * Both endpoints answer the plain, single-encoded { success } / { error }
         * shape the popups expect (SearchCustomers above is double-encoded for its
         * own historical client contract - the JS parser tolerates both).
         * ------------------------------------------------------------------- */

        // Longest subject stored for an appointment or task. Trimming here keeps an
        // over-long paste from failing the save at the database.
        private const int MaxSubjectLength = 255;

        // A quick-scheduled appointment is a half-hour slot; the user lengthens it
        // in the calendar when it needs to be longer.
        private const int DefaultAppointmentMinutes = 30;

        // Start time used when a date is picked but the time is left empty.
        private const int DefaultAppointmentHour = 9;

        // Upper bound for the "Due" offset the New task popup sends, in days.
        private const int MaxTaskDueDays = 365;

        /// <summary>
        /// Creates a calendar appointment for one customer ("Schedule" quick action).
        /// Persisted through the MAppointmentsInfo M-class - never a raw INSERT - as
        /// AppointmentsInfo with IsTask='N'. That table carries no C_BPartner_ID, so
        /// the customer is linked polymorphically through AD_Table_ID + Record_ID,
        /// the same way the account panel records a meeting.
        /// </summary>
        /// <param name="C_BPartner_ID">Customer the appointment belongs to.</param>
        /// <param name="title">Appointment subject.</param>
        /// <param name="date">Start date as the ISO yyyy-MM-dd the date input posts.</param>
        /// <param name="time">Optional start time as the ISO HH:mm the time input posts.</param>
        /// <returns>JSON { success, AppointmentsInfo_ID } or { error }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult SaveAppointment(int C_BPartner_ID, string title, string date, string time)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (C_BPartner_ID <= 0)
            {
                return Json(new
                {
                    error = Msg.GetMsg(ctx, "VAS_120_InvalidCustomer") ?? "Invalid customer."
                }, JsonRequestBehavior.AllowGet);
            }

            title = (title ?? "").Trim();
            if (title.Length == 0)
            {
                return Json(new
                {
                    error = Msg.GetMsg(ctx, "VAS_120_TitleRequired") ?? "Please enter a title."
                }, JsonRequestBehavior.AllowGet);
            }

            DateTime startDate;
            if (!TryParseStart(date, time, out startDate))
            {
                return Json(new
                {
                    error = Msg.GetMsg(ctx, "VAS_120_DateRequired") ?? "Please pick a date."
                }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                MAppointmentsInfo appointment = new MAppointmentsInfo(ctx, 0, null);
                appointment.SetIsTask(false);
                appointment.SetSubject(Truncate(title, MaxSubjectLength));
                appointment.SetStartDate(startDate);
                appointment.SetEndDate(startDate.AddMinutes(DefaultAppointmentMinutes));
                ApplyOwnerAndCustomer(ctx, appointment, C_BPartner_ID);

                if (!appointment.Save())
                {
                    return Json(new
                    {
                        error = SaveErrorMessage(ctx, "VAS_120_ScheduleSaveFailed", "Could not schedule the appointment.")
                    }, JsonRequestBehavior.AllowGet);
                }

                return Json(new { success = true, AppointmentsInfo_ID = appointment.Get_ID() }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_120_CustomerSearchWidget.SaveAppointment", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Creates a to-do for one customer ("Create task" quick action). Tasks share
        /// the AppointmentsInfo table with appointments and are discriminated by
        /// IsTask='Y'; the due date lives in EndDate, the column the task readers
        /// project as DueDate. Written through the M-class, never a raw INSERT.
        /// </summary>
        /// <param name="C_BPartner_ID">Customer the task belongs to.</param>
        /// <param name="subject">What needs to happen.</param>
        /// <param name="dueDays">Whole days from today; the popup offers 0/1/3/7.</param>
        /// <returns>JSON { success, AppointmentsInfo_ID } or { error }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult SaveTask(int C_BPartner_ID, string subject, int dueDays)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (C_BPartner_ID <= 0)
            {
                return Json(new
                {
                    error = Msg.GetMsg(ctx, "VAS_120_InvalidCustomer") ?? "Invalid customer."
                }, JsonRequestBehavior.AllowGet);
            }

            subject = (subject ?? "").Trim();
            if (subject.Length == 0)
            {
                return Json(new
                {
                    error = Msg.GetMsg(ctx, "VAS_120_TaskRequired") ?? "Please enter a task."
                }, JsonRequestBehavior.AllowGet);
            }

            if (dueDays < 0) { dueDays = 0; }
            if (dueDays > MaxTaskDueDays) { dueDays = MaxTaskDueDays; }

            try
            {
                // The popup sends a relative offset rather than a date, so the due
                // day is resolved here: a browser in another time zone still stores
                // the day the user picked. End of that day, so a task due "Today"
                // is not already overdue.
                DateTime dueDate = DateTime.Today.AddDays(dueDays).AddDays(1).AddMinutes(-1);

                MAppointmentsInfo task = new MAppointmentsInfo(ctx, 0, null);
                task.SetIsTask(true);
                task.SetSubject(Truncate(subject, MaxSubjectLength));
                task.SetStartDate(DateTime.Now);
                task.SetEndDate(dueDate);
                task.SetTaskStatus(0);
                ApplyOwnerAndCustomer(ctx, task, C_BPartner_ID);

                if (!task.Save())
                {
                    return Json(new
                    {
                        error = SaveErrorMessage(ctx, "VAS_120_TaskSaveFailed", "Could not add the task.")
                    }, JsonRequestBehavior.AllowGet);
                }

                return Json(new { success = true, AppointmentsInfo_ID = task.Get_ID() }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_120_CustomerSearchWidget.SaveTask", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Applies the defaults shared by a quick-created appointment and task: the
        /// logged-in user owns it, it starts open, and it points at the customer
        /// through AD_Table_ID + Record_ID (AppointmentsInfo has no C_BPartner_ID).
        /// </summary>
        /// <param name="ctx">Authenticated request context.</param>
        /// <param name="entry">The unsaved appointment/task.</param>
        /// <param name="bPartnerId">Customer to attach it to.</param>
        private void ApplyOwnerAndCustomer(Ctx ctx, MAppointmentsInfo entry, int bPartnerId)
        {
            entry.SetAD_User_ID(ctx.GetAD_User_ID());
            entry.SetIsClosed(false);
            entry.SetIsPrivate(false);
            entry.SetIsRead(true);

            int bPartnerTableId = MTable.Get_Table_ID("C_BPartner");
            if (bPartnerTableId > 0)
            {
                entry.SetAD_Table_ID(bPartnerTableId);
            }
            entry.SetRecord_ID(bPartnerId);

            if (entry.GetAD_Org_ID() == 0)
            {
                entry.SetAD_Org_ID(ctx.GetAD_Org_ID());
            }
        }

        /// <summary>
        /// Combines the ISO date and optional ISO time the browser's date/time inputs
        /// post (yyyy-MM-dd and HH:mm) into one start instant. Parsing is exact and
        /// invariant-culture, so the stored value never depends on the server's
        /// regional settings.
        /// </summary>
        /// <param name="date">Date part; required.</param>
        /// <param name="time">Time part; optional, defaults to the morning hour.</param>
        /// <param name="startDate">Parsed start instant on success.</param>
        /// <returns>False when the date is missing or unparseable.</returns>
        private static bool TryParseStart(string date, string time, out DateTime startDate)
        {
            startDate = DateTime.MinValue;

            DateTime day;
            if (!DateTime.TryParseExact((date ?? "").Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            {
                return false;
            }

            TimeSpan timeOfDay = TimeSpan.FromHours(DefaultAppointmentHour);

            string trimmedTime = (time ?? "").Trim();
            if (trimmedTime.Length > 0)
            {
                DateTime parsedTime;
                // Some browsers include seconds; accept both shapes.
                if (DateTime.TryParseExact(trimmedTime, new[] { "HH:mm", "HH:mm:ss" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTime))
                {
                    timeOfDay = parsedTime.TimeOfDay;
                }
            }

            startDate = day.Add(timeOfDay);
            return true;
        }

        /// <summary>
        /// Message for a failed M-class save: the framework's own validation text
        /// when it captured one, else the widget's generic fallback.
        /// </summary>
        private static string SaveErrorMessage(Ctx ctx, string messageKey, string fallback)
        {
            ValueNamePair frameworkError = VLogger.RetrieveError();
            if (frameworkError != null && !string.IsNullOrEmpty(frameworkError.GetName()))
            {
                return frameworkError.GetName();
            }

            return Msg.GetMsg(ctx, messageKey) ?? fallback;
        }

        /// <summary>
        /// Caps a value at the column's stored length.
        /// </summary>
        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) { return value; }
            return value.Substring(0, maxLength);
        }

        /// <summary>
        /// Builds the autosuggest rows and total count for the given search text.
        /// </summary>
        /// <param name="ctx">Authenticated request context (tenant/org/role).</param>
        /// <param name="searchText">Raw user input; trimmed and length-checked here.</param>
        /// <param name="maxRows">Requested cap, clamped to seven.</param>
        /// <returns>Populated result; empty rows when the text is too short.</returns>
        private CustomerSearchResult SearchCustomersData(Ctx ctx, string searchText, int maxRows)
        {
            CustomerSearchResult result = new CustomerSearchResult
            {
                Rows = new List<CustomerSearchRow>(),
                Total = 0,
                MinLength = MinSearchLength,
                // Cached by MTable, so this is a dictionary lookup rather than a query.
                BPartnerTableId = MTable.Get_Table_ID("C_BPartner")
            };

            if (ctx == null) { return result; }

            searchText = (searchText ?? "").Trim();
            if (searchText.Length < MinSearchLength) { return result; }

            if (maxRows <= 0 || maxRows > MaxSuggestRows) { maxRows = MaxSuggestRows; }

            int clientId = ctx.GetAD_Client_ID();
            string upperText = searchText.ToUpperInvariant();
            string likeValue = "%" + upperText + "%";
            string prefixValue = upperText + "%";
            string language = GetLanguage(ctx);

            // Shared CTE (ranked primary contact + secured customer_search body).
            // MRole is applied to the main physical table alias "bp" only.
            string cteSql = BuildCustomerSearchCte(ctx);

            string suggestSql = @"
                " + cteSql + @"
                SELECT CustomerSearch.Id,
                       CustomerSearch.Value,
                       CustomerSearch.Name,
                       CustomerSearch.Contact_Id,
                       CustomerSearch.Contact,
                       CustomerSearch.Segment_Id,
                       CustomerSearch.Segment,
                       CustomerSearch.Owner_Id,
                       CustomerSearch.Rep,
                       CustomerSearch.Tier_Code,
                       CustomerSearch.Tier_Name
                FROM CustomerSearch
                ORDER BY CustomerSearch.Relevance_Rank,
                         CustomerSearch.Name,
                         CustomerSearch.Id
                OFFSET 0 ROWS FETCH NEXT @Max_Rows ROWS ONLY";

            string countSql = @"
                " + cteSql + @"
                SELECT COUNT(1) AS Total_Count
                FROM CustomerSearch";

            // Rows.
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(suggestSql, BuildSearchParameters(clientId, likeValue, upperText, prefixValue, language, maxRows));
                while (dr != null && dr.Read())
                {
                    string tierCode = Util.GetValueOfString(dr["Tier_Code"]);
                    string tierName = Util.GetValueOfString(dr["Tier_Name"]);
                    string tier = MapTier(tierCode, tierName);
                    result.Rows.Add(new CustomerSearchRow
                    {
                        Id = Util.GetValueOfInt(dr["Id"]),
                        Value = Util.GetValueOfString(dr["Value"]),
                        Name = Util.GetValueOfString(dr["Name"]),
                        ContactId = Util.GetValueOfInt(dr["Contact_Id"]),
                        Contact = Util.GetValueOfString(dr["Contact"]),
                        SegmentId = Util.GetValueOfInt(dr["Segment_Id"]),
                        Segment = Util.GetValueOfString(dr["Segment"]),
                        OwnerId = Util.GetValueOfInt(dr["Owner_Id"]),
                        Rep = Util.GetValueOfString(dr["Rep"]),
                        TierCode = tierCode,
                        Tier = tier,
                        // Key-client is derived only from an explicitly approved
                        // configuration; never inferred silently (Querry.md).
                        IsKey = false
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            // Total (drives "See all N matches").
            IDataReader countReader = null;
            try
            {
                // The count query does not reference @Max_Rows, so it is bound with
                // the shared predicate parameters only (no unused bind).
                countReader = DB.ExecuteReader(countSql, BuildSearchParameters(clientId, likeValue, upperText, prefixValue, language, null));
                if (countReader != null && countReader.Read())
                {
                    result.Total = Util.GetValueOfInt(countReader["Total_Count"]);
                }
            }
            finally
            {
                CloseReader(countReader);
            }

            return result;
        }

        /// <summary>
        /// Builds the shared "WITH RankedContacts AS (...), CustomerSearch AS (...)"
        /// prologue. RankedContacts picks one deterministic contact per business
        /// partner (an e-mailed contact first, then most recently updated).
        /// CustomerSearch is the tenant/customer-scoped, search-filtered body with
        /// a relevance rank; MRole record/tenant access is injected on the main
        /// physical table (alias "bp") only, per the CTE MRole rule.
        /// </summary>
        /// The tier tag is resolved from the application dictionary rather than a
        /// hard-coded table: AD_Table -> AD_Column('Rating') -> AD_Ref_List gives the
        /// tenant's own label for the stored Rating code, translated through
        /// AD_Ref_List_Trl when the session language has an entry. A customer whose
        /// Rating is null, inactive or absent from the list yields an empty
        /// Tier_Name and the UI shows no tag - an unknown rating is still never
        /// labelled (2026-08-07).
        /// <param name="ctx">Request context supplying the role for AddAccessSQL.</param>
        /// <returns>The two-CTE prologue string (no trailing outer SELECT).</returns>
        private string BuildCustomerSearchCte(Ctx ctx)
        {
            // Secondary source: AD_User is NOT MRole-filtered (CTE rule) but still
            // carries the mandatory IsActive + tenant predicates.
            string rankedContactsSql = @"
                SELECT Contact.C_BPartner_ID,
                       Contact.AD_User_ID,
                       Contact.Name AS Contact_Name,
                       Contact.EMail AS Contact_EMail,
                       ROW_NUMBER() OVER (
                           PARTITION BY Contact.C_BPartner_ID
                           ORDER BY CASE WHEN Contact.EMail IS NOT NULL THEN 0 ELSE 1 END,
                                    Contact.Updated DESC,
                                    Contact.AD_User_ID
                       ) AS RN
                FROM AD_User Contact
                WHERE Contact.IsActive = 'Y'
                  AND Contact.AD_Client_ID = @Contact_Client_ID";

            // Main physical table body. bp is the primary data source; grp / c /
            // owner are secondary joins used only to resolve display + search text.
            string customerSearchSql = @"
                SELECT bp.C_BPartner_ID AS Id,
                       bp.Value AS Value,
                       bp.Name AS Name,
                       c.AD_User_ID AS Contact_Id,
                       COALESCE(c.Contact_Name, bp.Name, N'') AS Contact,
                       COALESCE(c.Contact_EMail, bp.EMail, N'') AS Contact_Email,
                       grp.C_BP_Group_ID AS Segment_Id,
                       COALESCE(grp.Name, N'') AS Segment,
                       owner.AD_User_ID AS Owner_Id,
                       COALESCE(owner.Name, N'') AS Rep,
                       bp.Rating AS Tier_Code,
                       COALESCE(RatingTrl.Name, RatingList.Name, N'') AS Tier_Name,
                       CASE
                           WHEN UPPER(COALESCE(bp.Name, N'')) = @Search_Exact_Name THEN 0
                           WHEN UPPER(COALESCE(bp.Value, N'')) = @Search_Exact_Value THEN 1
                           WHEN UPPER(COALESCE(bp.Name, N'')) LIKE @Search_Prefix_Name THEN 2
                           WHEN UPPER(COALESCE(c.Contact_Name, bp.Name, N'')) LIKE @Search_Prefix_Contact THEN 3
                           ELSE 4
                       END AS Relevance_Rank
                FROM C_BPartner bp
                LEFT OUTER JOIN C_BP_Group grp ON (grp.C_BP_Group_ID = bp.C_BP_Group_ID AND grp.AD_Client_ID = bp.AD_Client_ID AND grp.IsActive = 'Y')
                LEFT OUTER JOIN RankedContacts c ON (c.C_BPartner_ID = bp.C_BPartner_ID AND c.RN = 1)
                LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID = bp.SalesRep_ID AND owner.AD_Client_ID = bp.AD_Client_ID AND owner.IsActive = 'Y')
                LEFT OUTER JOIN AD_Table BPartnerTable ON (BPartnerTable.TableName = 'C_BPartner')
                LEFT OUTER JOIN AD_Column RatingColumn ON (RatingColumn.AD_Table_ID = BPartnerTable.AD_Table_ID AND RatingColumn.ColumnName = 'Rating' AND RatingColumn.IsActive = 'Y')
                LEFT OUTER JOIN AD_Ref_List RatingList ON (RatingList.AD_Reference_ID = RatingColumn.AD_Reference_Value_ID AND RatingList.Value = bp.Rating AND RatingList.IsActive = 'Y')
                LEFT OUTER JOIN AD_Ref_List_Trl RatingTrl ON (RatingTrl.AD_Ref_List_ID = RatingList.AD_Ref_List_ID AND RatingTrl.AD_Language = @AD_Language)
                WHERE bp.IsActive = 'Y'
                  AND bp.IsCustomer = 'Y'
                  AND bp.AD_Client_ID = @Customer_Client_ID
                  AND (
                      UPPER(COALESCE(bp.Name, N'')) LIKE @Search_Like_Name
                      OR UPPER(COALESCE(bp.Value, N'')) LIKE @Search_Like_Value
                      OR UPPER(COALESCE(c.Contact_Name, bp.Name, N'')) LIKE @Search_Like_Contact
                      OR UPPER(COALESCE(c.Contact_EMail, bp.EMail, N'')) LIKE @Search_Like_Email
                      OR UPPER(COALESCE(grp.Name, N'')) LIKE @Search_Like_Segment
                      OR UPPER(COALESCE(owner.Name, N'')) LIKE @Search_Like_Rep
                  )";

            // MRole tenant + record access on the main physical table alias only.
            customerSearchSql = MRole.GetDefault(ctx).AddAccessSQL(
                customerSearchSql,
                "bp",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            return @"WITH RankedContacts AS (
                    " + rankedContactsSql + @"
                ),
                CustomerSearch AS (
                    " + customerSearchSql + @"
                )";
        }

        /// <summary>
        /// Fresh parameter array for one command execution (a SqlParameter cannot be
        /// shared across two commands, so each query builds its own).
        /// </summary>
        /// The app's Oracle layer never sets BindByName, so ODP.NET binds BY POSITION:
        /// every placeholder OCCURRENCE needs its own parameter, listed in the order the
        /// occurrences appear in the assembled statement, and no parameter may be
        /// supplied that the statement does not use. That is why the repeated search
        /// values are bound under one name per occurrence (_Name / _Value / _Contact /
        /// _Email / _Segment / _Rep suffixes) rather than a single shared @Search_Like -
        /// re-using one name raised ORA-01008 and killed the whole search (2026-08-25).
        /// <param name="clientId">Tenant id, bound once per occurrence.</param>
        /// <param name="likeValue">"%TEXT%" for the six contains-branches.</param>
        /// <param name="exactValue">Upper-cased raw text for the two equality ranks.</param>
        /// <param name="prefixValue">"TEXT%" for the two starts-with ranks.</param>
        /// <param name="language">Session language for the tier-label translation.</param>
        /// <param name="maxRows">Row cap for the suggest fetch; null for the count.</param>
        private SqlParameter[] BuildSearchParameters(int clientId, string likeValue, string exactValue, string prefixValue, string language, int? maxRows)
        {
            List<SqlParameter> parameters = new List<SqlParameter>
            {
                // 1 - RankedContacts CTE.
                new SqlParameter("@Contact_Client_ID", clientId),
                // 2-5 - Relevance_Rank CASE in the CustomerSearch select list.
                new SqlParameter("@Search_Exact_Name", SqlDbType.NVarChar) { Value = exactValue },
                new SqlParameter("@Search_Exact_Value", SqlDbType.NVarChar) { Value = exactValue },
                new SqlParameter("@Search_Prefix_Name", SqlDbType.NVarChar) { Value = prefixValue },
                new SqlParameter("@Search_Prefix_Contact", SqlDbType.NVarChar) { Value = prefixValue },
                // 6 - AD_Ref_List_Trl join (tier label translation).
                new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = language },
                // 7-13 - CustomerSearch WHERE clause.
                new SqlParameter("@Customer_Client_ID", clientId),
                new SqlParameter("@Search_Like_Name", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Like_Value", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Like_Contact", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Like_Email", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Like_Segment", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Like_Rep", SqlDbType.NVarChar) { Value = likeValue }
            };

            // 14 - @Max_Rows closes the suggest fetch; the count query omits it (an
            // unused parameter would break positional binding just as a missing one does).
            if (maxRows.HasValue)
            {
                parameters.Add(new SqlParameter("@Max_Rows", maxRows.Value));
            }

            return parameters.ToArray();
        }

        /// <summary>
        /// Resolves the display tier for one row. An explicit tenant mapping in
        /// CustomerTierByRating wins when present; otherwise the tenant's own
        /// AD_Ref_List label for the Rating code (already translated by the query)
        /// is used. Returns null when the rating is unset or carries no list entry,
        /// so the UI shows no tag and an unknown rating is never labelled.
        /// </summary>
        /// <param name="ratingCode">Stored Rating value (may be null/empty).</param>
        /// <param name="ratingName">AD_Ref_List/Trl name for that code (may be empty).</param>
        /// <returns>The configured tier, else the folded star tier, else the
        /// reference-list label, else null.</returns>
        private string MapTier(string ratingCode, string ratingName)
        {
            if (string.IsNullOrEmpty(ratingCode)) { return null; }

            // An explicit per-tenant override always wins.
            string tier;
            if (CustomerTierByRating.TryGetValue(ratingCode, out tier)) { return tier; }

            // Star-rating lists (the stock _Rating reference labels its entries
            // "-", "*", "**", ...) are folded onto the three display tiers per the
            // approved business rule: 3+ stars Platinum, 2 Gold, 1 Silver. Reading
            // the star COUNT off the label keeps this independent of the stored
            // Value codes, which differ between tenants.
            int stars = CountRatingStars(ratingName);
            if (stars >= 3) { return TierPlatinum; }
            if (stars == 2) { return TierGold; }
            if (stars == 1) { return TierSilver; }

            // Not a star list: fall back to the tenant's own label (e.g.
            // "Preferred"). The zero-star entry is "not rated", so it -- like a
            // blank label -- draws no tag; an unknown rating is still never named.
            if (string.IsNullOrWhiteSpace(ratingName)) { return null; }

            string trimmed = ratingName.Trim();
            return trimmed == "-" ? null : trimmed;
        }

        /// <summary>
        /// Counts star glyphs in a rating label. Both the ASCII asterisk used by the
        /// stock reference data and the unicode star some tenants store are counted,
        /// so the fold works whichever form the dictionary holds.
        /// </summary>
        /// <param name="ratingName">Reference-list label (may be null/empty).</param>
        /// <returns>Number of star glyphs; 0 when the label carries none.</returns>
        private static int CountRatingStars(string ratingName)
        {
            if (string.IsNullOrEmpty(ratingName)) { return 0; }

            int stars = 0;
            foreach (char character in ratingName)
            {
                // U+2605 BLACK STAR and U+2606 WHITE STAR alongside the ASCII
                // asterisk, so the count works whichever glyph the list uses.
                if (character == '*' || character == '★' || character == '☆')
                {
                    stars++;
                }
            }

            return stars;
        }

        /// <summary>
        /// Session language for the AD_Ref_List_Trl join, falling back to en_US.
        /// </summary>
        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();

            return string.IsNullOrWhiteSpace(language) ? "en_US" : language;
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

        private class CustomerSearchResult
        {
            public List<CustomerSearchRow> Rows { get; set; }
            public int Total { get; set; }
            public int MinLength { get; set; }

            /// <summary>
            /// AD_Table_ID of C_BPartner. The row quick actions hand this to the
            /// standard platform forms (VIS.AppointmentsForm / VIS.Email) as the
            /// record's table context. A dashboard widget - unlike a tab panel - has no
            /// framework-supplied table_ID, so it is resolved here and travels with the
            /// rows the actions sit on.
            /// </summary>
            public int BPartnerTableId { get; set; }
        }

        private class CustomerSearchRow
        {
            public int Id { get; set; }
            public string Value { get; set; }
            public string Name { get; set; }
            public int ContactId { get; set; }
            public string Contact { get; set; }
            public int SegmentId { get; set; }
            public string Segment { get; set; }
            public int OwnerId { get; set; }
            public string Rep { get; set; }
            public string TierCode { get; set; }
            public string Tier { get; set; }
            public bool IsKey { get; set; }
        }
    }
}
