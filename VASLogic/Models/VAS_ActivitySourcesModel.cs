/// <summary>
/// Module Name : VASLogic
/// Purpose     : The correspondence and engagement sources every overview
///               panel's Activity feed draws on, in one place:
///
///                 Appointments  AppointmentsInfo, IsTask = 'N'
///                 Tasks         AppointmentsInfo, IsTask = 'Y'
///                 Calls         VA048_CallDetails
///                 Letters       MailAttachment1,  AttachmentType = 'I'
///                 Mails         MailAttachment1,  AttachmentType <> 'I'
///
///               All five hang off their parent by AD_Table_ID + Record_ID, so
///               one loader serves every panel: the caller names its own table
///               and record and maps the neutral rows into its own activity
///               type. Ten copies of the same five statements is the thing this
///               exists to avoid.
///
///               Chat (CM_Chat / CM_ChatEntry) is deliberately NOT here. Every
///               panel already loads it, and its rows carry panel-specific
///               shaping; a second loader would only duplicate the entries.
///
/// Chronological development:
///   VAI163   2026-08-20  Created. Sources and filters follow
///                        VAS_105_AccountRightPanelModel.GetTimeline, which
///                        already read all of them for a business partner, and
///                        the guarded appointment reader in
///                        VAS_190_ProductOverviewRightPanelModel.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text.RegularExpressions;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// One correspondence / engagement entry, in a shape every panel can map
    /// into its own activity type. Fields not meaningful for a Kind are left at
    /// their defaults.
    /// </summary>
    public class VAS_ActivitySourceRow
    {
        /// <summary>appointment | task | call | letter | email</summary>
        public string    Kind       { get; set; }
        public int       RecordId   { get; set; }
        /// <summary>The row's headline: a meeting's subject, a call's note, a
        /// letter's or mail's title.</summary>
        public string    Title      { get; set; }
        public string    Body       { get; set; }
        public string    ActorName  { get; set; }
        /// <summary>When it happened — a meeting's start, everything else's
        /// create stamp (a letter's received date where it has one).</summary>
        public DateTime? EventTime  { get; set; }

        // Appointment / task
        public string    Location    { get; set; }
        public DateTime? StartDate   { get; set; }
        public DateTime? EndDate     { get; set; }
        public bool      IsClosed    { get; set; }
        public bool      IsCancelled { get; set; }

        // Letter / mail
        public string    MailTo     { get; set; }
        public string    MailCc     { get; set; }
        public string    MailBcc    { get; set; }
        public string    MailFrom   { get; set; }
        public bool      IsMailSent { get; set; }
    }

    /// <summary>
    /// Reads the shared activity sources for one record. Create one per request.
    /// </summary>
    public class VAS_ActivitySourcesModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_ActivitySourcesModel).FullName);

        // Whether each source is usable against this schema. Attempted rather
        // than gated on a dictionary guard, for the reason VAS_092 settled on
        // with its blanket lookup: the guard is itself a thing that can answer
        // "absent" while the data is right there. A genuinely missing table
        // throws ONCE and is remembered — the column set is a property of the
        // database, one per app instance, so a static is safe.
        // Null = not tried, false = unusable, true = it ran.
        private static bool? _apptUsable;
        private static bool? _callUsable;
        private static bool? _mailUsable;

        /// <summary>AD_Table_ID by table name — resolved once per app.</summary>
        private static readonly Dictionary<string, int> _tableIds =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _tableIdLock = new object();

        /// <summary>
        /// Every appointment, task, call and letter pinned to this record, newest
        /// first. Mails are included only when <paramref name="includeMail"/> is
        /// set — most panels already read their own, with the recipient and body
        /// detail their mail drawer needs.
        /// </summary>
        /// <param name="tableName">The panel's own table, e.g. "C_Order".</param>
        /// <param name="recordId">The record the panel is showing.</param>
        /// <param name="includeMail">Also read mails (AttachmentType &lt;&gt; 'I').
        /// For the panels that have no mail loader of their own.</param>
        public List<VAS_ActivitySourceRow> Load(string tableName, int recordId, bool includeMail)
        {
            List<VAS_ActivitySourceRow> rows = new List<VAS_ActivitySourceRow>();
            if (string.IsNullOrEmpty(tableName) || recordId <= 0) return rows;

            int tableId = TableId(tableName);
            if (tableId <= 0) return rows;

            LoadAppointments(tableId, recordId, rows);
            LoadCalls(tableId, recordId, rows);
            LoadMailAttachments(tableId, recordId, rows, includeMail);

            rows.Sort((a, b) =>
                b.EventTime.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.EventTime.GetValueOrDefault(DateTime.MinValue)));
            return rows;
        }

        /// <summary>AD_Table_ID for a table name, or 0. Cached for the app's life
        /// — the dictionary does not change under a running instance.</summary>
        private int TableId(string tableName)
        {
            lock (_tableIdLock)
            {
                int cached;
                if (_tableIds.TryGetValue(tableName, out cached)) return cached;
            }

            int id = 0;
            try
            {
                string sql = @"SELECT t.AD_Table_ID FROM AD_Table t
                                WHERE UPPER(t.TableName) = UPPER(@TableName)
                                  AND COALESCE(t.IsActive, 'Y') = 'Y'
                                ORDER BY t.AD_Table_ID";
                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@TableName", tableName) }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    id = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Table_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("TableId (" + tableName + "): " + ex.Message);
            }

            lock (_tableIdLock) { _tableIds[tableName] = id; }
            return id;
        }

        // ----------------------------------------------------------------- //
        //  Appointments and tasks — AppointmentsInfo                         //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Meetings and tasks logged against the record. IsTask is what separates
        /// the two: 'N' is an appointment, 'Y' a task.
        ///
        /// AppointmentsInfo stores one row per ATTENDEE, not one per meeting, so a
        /// five-person meeting would otherwise fill the feed with five identical
        /// entries. Collapsed on (StartDate, Subject) — the rows arrive ordered by
        /// id, so the first seen wins and the entry stays stable between refreshes.
        /// Follows VAS_190.
        /// </summary>
        private void LoadAppointments(int tableId, int recordId, List<VAS_ActivitySourceRow> rows)
        {
            if (_apptUsable == false) return;

            try
            {
                string sql = @"SELECT ai.AppointmentsInfo_ID,
                                      ai.Subject,
                                      ai.Description,
                                      ai.StartDate,
                                      ai.EndDate,
                                      ai.Location,
                                      COALESCE(ai.IsTask, 'N')      AS IsTask,
                                      COALESCE(ai.IsClosed, 'N')    AS IsClosed,
                                      COALESCE(ai.IsCancelled, 'N') AS IsCancelled,
                                      ai.Created,
                                      COALESCE(u.Name, cu.Name)     AS ActorName
                                 FROM AppointmentsInfo ai
                                 LEFT OUTER JOIN AD_User u  ON (u.AD_User_ID  = ai.AD_User_ID)
                                 LEFT OUTER JOIN AD_User cu ON (cu.AD_User_ID = ai.CreatedBy)
                                WHERE ai.AD_Table_ID = " + tableId + @"
                                  AND ai.Record_ID   = @Record_ID
                                  AND COALESCE(ai.IsActive, 'Y') = 'Y'
                                ORDER BY COALESCE(ai.StartDate, ai.Created) DESC,
                                         ai.AppointmentsInfo_ID DESC";
                DataSet ds = DB.ExecuteDataset(sql, RecordParam(recordId), null);
                _apptUsable = true;
                if (ds == null || ds.Tables.Count == 0) return;

                List<string> seenMeetings = new List<string>();
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    DateTime? start = Util.GetValueOfDateTime(r["StartDate"]);
                    string subject  = Util.GetValueOfString(r["Subject"]);
                    string key = (start.HasValue ? start.Value.ToString("yyyyMMddHHmm") : "")
                               + "|" + subject;
                    if (seenMeetings.Contains(key)) continue;
                    seenMeetings.Add(key);

                    bool isTask = Util.GetValueOfString(r["IsTask"]) == "Y";
                    rows.Add(new VAS_ActivitySourceRow
                    {
                        Kind        = isTask ? "task" : "appointment",
                        RecordId    = Util.GetValueOfInt(r["AppointmentsInfo_ID"]),
                        Title       = subject,
                        Body        = Util.GetValueOfString(r["Description"]),
                        Location    = Util.GetValueOfString(r["Location"]),
                        StartDate   = start,
                        EndDate     = Util.GetValueOfDateTime(r["EndDate"]),
                        IsClosed    = Util.GetValueOfString(r["IsClosed"]) == "Y",
                        IsCancelled = Util.GetValueOfString(r["IsCancelled"]) == "Y",
                        ActorName   = Util.GetValueOfString(r["ActorName"]),
                        // Dated by when it is SCHEDULED, which is what a reader
                        // scanning a timeline for a meeting is looking for; the
                        // create stamp only stands in where there is no start.
                        EventTime   = start ?? Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                // No AppointmentsInfo on this schema. Recorded so the next record
                // skips the attempt.
                _apptUsable = false;
                _log.Severe("LoadAppointments (table=" + tableId + ", record=" + recordId + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  Calls — VA048_CallDetails                                         //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Calls logged against the record. Every call lives in this one table —
        /// there is no type to distinguish, so no filter beyond the record link.
        ///
        /// Skipped outright where the VA048 module is not installed, which is the
        /// cheap answer; the attempt-and-remember flag covers a schema that
        /// reports the module but lacks the table.
        /// </summary>
        private void LoadCalls(int tableId, int recordId, List<VAS_ActivitySourceRow> rows)
        {
            if (_callUsable == false) return;

            try
            {
                if (!Env.IsModuleInstalled("VA048_")) { _callUsable = false; return; }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadCalls/module: " + ex.Message);
                _callUsable = false;
                return;
            }

            try
            {
                // The note is the call's substance; the number dialled stands in
                // where nobody wrote one, so the row always says something.
                string sql = @"SELECT cd.VA048_CallDetails_ID,
                                      cd.VA048_CallNotes,
                                      cd.VA048_To,
                                      cd.Created,
                                      u.Name AS ActorName
                                 FROM VA048_CallDetails cd
                                 LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = cd.CreatedBy)
                                WHERE cd.AD_Table_ID = " + tableId + @"
                                  AND cd.Record_ID   = @Record_ID
                                  AND COALESCE(cd.IsActive, 'Y') = 'Y'
                                ORDER BY cd.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, RecordParam(recordId), null);
                _callUsable = true;
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string note = Util.GetValueOfString(r["VA048_CallNotes"]);
                    string to   = Util.GetValueOfString(r["VA048_To"]);
                    rows.Add(new VAS_ActivitySourceRow
                    {
                        Kind      = "call",
                        RecordId  = Util.GetValueOfInt(r["VA048_CallDetails_ID"]),
                        Title     = !string.IsNullOrEmpty(note) ? note : to,
                        Body      = note,
                        MailTo    = to,
                        ActorName = Util.GetValueOfString(r["ActorName"]),
                        EventTime = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _callUsable = false;
                _log.Severe("LoadCalls (table=" + tableId + ", record=" + recordId + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  Letters and mails — MailAttachment1                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Correspondence filed against the record. AttachmentType separates the
        /// two kinds: 'I' is an inbound LETTER, anything else a mail.
        ///
        /// Mails are read as "not 'I'" rather than as exactly 'M'. The value varies
        /// between installations — some leave it null — and demanding 'M' hides
        /// mails that are really there, which is why the panels' own mail loaders
        /// never filtered on it at all. Read this way the two kinds partition the
        /// table: nothing is hidden and nothing is counted twice.
        ///
        /// A letter is dated by when it was RECEIVED where the column carries one,
        /// since that is the event; a mail by when it was sent.
        /// </summary>
        private void LoadMailAttachments(int tableId, int recordId,
                                         List<VAS_ActivitySourceRow> rows, bool includeMail)
        {
            if (_mailUsable == false) return;

            // A letter is always wanted; mails only where the caller has no loader
            // of its own.
            string kindFilter = includeMail
                ? ""
                : " AND COALESCE(ma.AttachmentType, 'M') = 'I'";

            try
            {
                string sql = @"SELECT ma.MailAttachment1_ID,
                                      ma.AttachmentType,
                                      ma.Title,
                                      ma.TextMsg,
                                      ma.MailAddress,
                                      ma.MailAddressCc,
                                      ma.MailAddressBcc,
                                      ma.MailAddressFrom,
                                      ma.IsMailSent,
                                      ma.DateMailReceived,
                                      ma.Created,
                                      u.Name AS ActorName
                                 FROM MailAttachment1 ma
                                 LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy)
                                WHERE ma.AD_Table_ID = " + tableId + @"
                                  AND ma.Record_ID   = @Record_ID
                                  AND COALESCE(ma.IsActive, 'Y') = 'Y'"
                                  + kindFilter + @"
                                ORDER BY ma.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, RecordParam(recordId), null);
                _mailUsable = true;
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    bool isLetter = Util.GetValueOfString(r["AttachmentType"]) == "I";
                    DateTime? received = Util.GetValueOfDateTime(r["DateMailReceived"]);
                    DateTime? created  = Util.GetValueOfDateTime(r["Created"]);

                    rows.Add(new VAS_ActivitySourceRow
                    {
                        Kind       = isLetter ? "letter" : "email",
                        RecordId   = Util.GetValueOfInt(r["MailAttachment1_ID"]),
                        Title      = Util.GetValueOfString(r["Title"]),
                        // Flattened here rather than by each caller: the panels
                        // render a body as TEXT, and a mail sent as HTML stores its
                        // markup in TextMsg.
                        Body       = MailBodyToText(Util.GetValueOfString(r["TextMsg"])),
                        MailTo     = Util.GetValueOfString(r["MailAddress"]),
                        MailCc     = Util.GetValueOfString(r["MailAddressCc"]),
                        MailBcc    = Util.GetValueOfString(r["MailAddressBcc"]),
                        MailFrom   = Util.GetValueOfString(r["MailAddressFrom"]),
                        IsMailSent = Util.GetValueOfString(r["IsMailSent"]) == "Y",
                        ActorName  = Util.GetValueOfString(r["ActorName"]),
                        EventTime  = (isLetter && received.HasValue) ? received : created
                    });
                }
            }
            catch (Exception ex)
            {
                _mailUsable = false;
                _log.Severe("LoadMailAttachments (table=" + tableId + ", record=" + recordId + "): " + ex.Message);
            }
        }

        /// <summary>Single-parameter helper for the record-scoped queries. The
        /// table id is inlined — it is an int this class resolved itself, and
        /// binding it a second time would give positional binding an unfilled
        /// placeholder.</summary>
        private SqlParameter[] RecordParam(int recordId)
        {
            return new SqlParameter[] { new SqlParameter("@Record_ID", recordId) };
        }

        // ----------------------------------------------------------------- //
        //  Mail body -> readable text                                        //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Cheap "is this markup" test — a real tag, not a stray '&lt;' in a plain
        /// text mail ("qty &lt; 10"), so a plain body is left untouched.
        /// </summary>
        private static readonly Regex HTML_BODY = new Regex(
            @"<\s*/?\s*(html|body|head|br|p|div|table|thead|tbody|tr|td|th|span|a|img|b|i|u"
            + @"|strong|em|ul|ol|li|h[1-6]|font|style|script)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Flattens an HTML mail body to readable plain text: script / style
        /// dropped, block ends turned into line breaks, tags stripped, entities
        /// decoded and runs of blank lines collapsed. A body that is already plain
        /// text is returned unchanged. Lifted from VAS_098, which needed exactly
        /// this for the same column.
        /// </summary>
        private static string MailBodyToText(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            if (!HTML_BODY.IsMatch(body)) return body.Trim();

            try
            {
                string s = body;
                s = Regex.Replace(s, @"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>",
                                  " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(p|div|tr|li|h[1-6]|table)\s*>", "\n",
                                  RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(td|th)\s*>", "\t", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<[^>]+>", "");
                s = WebUtility.HtmlDecode(s);
                s = s.Replace("\r\n", "\n").Replace("\r", "\n");
                s = Regex.Replace(s, @"[ \t]+\n", "\n");
                s = Regex.Replace(s, @"\n{3,}", "\n\n");
                return s.Trim();
            }
            catch
            {
                return body;
            }
        }
    }
}
