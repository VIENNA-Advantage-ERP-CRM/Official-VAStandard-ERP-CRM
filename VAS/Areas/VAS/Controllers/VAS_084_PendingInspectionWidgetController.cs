using Newtonsoft.Json;
using System;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Pending Inspection (Material Receipt / GRN dashboard KPI)
    /// Purpose     : KPI = COUNT(DISTINCT M_InOutLineConfirm) of vendor-GRN receipt
    ///               confirmation lines that are flagged for quality check
    ///               (VA010_QualCheckMArk = 'Y') but whose QA actual value is still
    ///               empty (no VA010_ShipConfParameters result yet) - i.e. lines
    ///               still on QA hold / awaiting inspection. Read-only.
    ///               MRole is applied to the primary fetched table
    ///               (M_InOutLineConfirm); the other tables are join/filter only.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    /// </summary>
    public class VAS_084_PendingInspectionWidgetController : Controller
    {
        /// <summary>
        /// KPI tile data: count of GRN confirmation lines awaiting QA inspection.
        /// </summary>
        /// <returns>JSON { pendingInspection }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPendingInspection()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string lineConfirmTable = GetQATableName("M_InOutLineConfirm");
            string confirmTable = GetQATableName("M_InOutConfirm");
            string inOutLineTable = GetQATableName("M_InOutLine");
            string inOutTable = GetQATableName("M_InOut");
            string qaTable = GetQATableName("VA010_ShipConfParameters");

            string sql = @"
                SELECT COUNT(DISTINCT LineConfirm.M_InOutLineConfirm_ID) AS Pending_Inspection_Count
                FROM " + lineConfirmTable + @" LineConfirm
                INNER JOIN " + confirmTable + @" Confirm ON (Confirm.M_InOutConfirm_ID=LineConfirm.M_InOutConfirm_ID)
                INNER JOIN " + inOutLineTable + @" InOutLine ON (InOutLine.M_InOutLine_ID=LineConfirm.M_InOutLine_ID)
                INNER JOIN " + inOutTable + @" InOut ON (InOut.M_InOut_ID=InOutLine.M_InOut_ID)
                LEFT OUTER JOIN " + qaTable + @" QAParam ON (QAParam.M_InOutLineConfirm_ID=LineConfirm.M_InOutLineConfirm_ID AND QAParam.IsActive='Y')
                WHERE LineConfirm.IsActive='Y'
                  AND Confirm.IsActive='Y'
                  AND InOutLine.IsActive='Y'
                  AND InOut.IsActive='Y'
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND LineConfirm.VA010_QualCheckMArk='Y'
                  AND (QAParam.VA010_ActualValue IS NULL OR QAParam.VA010_ActualValue='')
                  AND COALESCE(Confirm.Processed,'N')<>'Y'
                  AND COALESCE(Confirm.DocStatus,'DR') IN ('DR','IP')";

            /* MRole supplies tenant + organization access. Applied only to the
               primary fetched table (M_InOutLineConfirm); the joined Confirm /
               InOutLine / InOut / QAParam tables are filter/comparison sources
               (shared Prompt_Instructions CTE/self-join rule). */
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "LineConfirm",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            IDataReader dr = null;

            try
            {
                int pendingInspection = 0;

                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    pendingInspection = Util.GetValueOfInt(dr["Pending_Inspection_Count"]);
                }

                var result = new
                {
                    pendingInspection = pendingInspection
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        private string GetQATableName(string tableName)
        {
            string owner = GetQASchemaOwner();
            return string.IsNullOrEmpty(owner) ? tableName : owner + "." + tableName;
        }

        private string GetQASchemaOwner()
        {
            if (HasColumn("M_InOutLineConfirm", "VA010_QualCheckMArk") && HasTable("VA010_ShipConfParameters"))
            {
                return "";
            }

            if (DB.IsPostgreSQL())
            {
                return "";
            }

            string sql = @"
                SELECT OwnerName
                FROM (
                    SELECT LineConfirmColumns.Owner AS OwnerName
                    FROM ALL_TAB_COLUMNS LineConfirmColumns
                    INNER JOIN ALL_TABLES QAParams
                        ON QAParams.Owner=LineConfirmColumns.Owner
                       AND QAParams.Table_Name='VA010_SHIPCONFPARAMETERS'
                    WHERE LineConfirmColumns.Table_Name='M_INOUTLINECONFIRM'
                      AND LineConfirmColumns.Column_Name='VA010_QUALCHECKMARK'
                    ORDER BY CASE WHEN LineConfirmColumns.Owner=USER THEN 0 ELSE 1 END
                )
                WHERE ROWNUM=1";

            string owner = Util.GetValueOfString(DB.ExecuteScalar(sql, null, null));
            return IsSafeDbIdentifier(owner) ? owner : "";
        }

        private bool HasTable(string tableName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.tables
                    WHERE UPPER(table_name)=UPPER(@TableName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TABLES
                    WHERE TABLE_NAME=UPPER(@TableName)";
            }

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new[] { new System.Data.SqlClient.SqlParameter("@TableName", tableName) }, null)) > 0;
        }

        private bool HasColumn(string tableName, string columnName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.columns
                    WHERE UPPER(table_name)=UPPER(@TableName)
                      AND UPPER(column_name)=UPPER(@ColumnName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME=UPPER(@TableName)
                      AND COLUMN_NAME=UPPER(@ColumnName)";
            }

            System.Data.SqlClient.SqlParameter[] parameters =
            {
                new System.Data.SqlClient.SqlParameter("@TableName", tableName),
                new System.Data.SqlClient.SqlParameter("@ColumnName", columnName)
            };
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null)) > 0;
        }

        private bool IsSafeDbIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
