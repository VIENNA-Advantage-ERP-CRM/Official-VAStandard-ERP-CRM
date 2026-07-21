using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : QA Holds (Material Receipt / GRN dashboard)
    /// Purpose     : 3x2 paged list of GRN receipt confirmation lines currently
    ///               held for quality inspection. A row opens the Quality Control
    ///               form where the QA actual value, QA/QC date and remark can be
    ///               saved against VA010_ShipConfParameters. The widget depends
    ///               on the optional VA010 quality module and returns an empty
    ///               state when that schema is not installed.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-20 Created
    /// </summary>
    public class VAS_086_QAHoldsWidgetController : Controller
    {
        private const string ActualWorkingFine = "Working Fine";
        private const string ActualNotSatisfactory = "Not Satisfactory";

        /// <summary>
        /// One page of unresolved QA holds for vendor GRN confirmation lines.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetQAHolds(int pageNo = 1, int pageSize = 5)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 5; }
            if (pageSize > 50) { pageSize = 50; }

            string schemaMessage;
            if (!HasRequiredQASchema(out schemaMessage))
            {
                return Json(JsonConvert.SerializeObject(new
                {
                    rows = new List<object>(),
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = 0,
                    totalPages = 0,
                    missingSchema = true,
                    message = schemaMessage
                }), JsonRequestBehavior.AllowGet);
            }

            int offset = (pageNo - 1) * pageSize;

            string lineConfirmTable = GetQATableName("M_InOutLineConfirm");
            string confirmTable = GetQATableName("M_InOutConfirm");
            string inOutLineTable = GetQATableName("M_InOutLine");
            string inOutTable = GetQATableName("M_InOut");
            string bPartnerTable = GetQATableName("C_BPartner");
            string productTable = GetQATableName("M_Product");
            string qaTable = GetQATableName("VA010_ShipConfParameters");
            string testParamTable = GetQATableName("VA010_TestParameter");
            string acceptableTable = GetQATableName("VA010_TestPrmtrList");

            string testParamDisplayColumn = FindDisplayColumn(testParamTable, new[] { "VA010_TestPrmtrName", "Name", "Description", "Value" });
            string acceptableDisplayColumn = FindDisplayColumn(acceptableTable, new[] { "VA010_ParameterValue", "Name", "Description", "Value" });

            string testParamNameExpr = !string.IsNullOrEmpty(testParamDisplayColumn) ? "TestParam." + testParamDisplayColumn : "CAST(NULL AS VARCHAR(255))";
            string acceptableNameExpr = !string.IsNullOrEmpty(acceptableDisplayColumn) ? "AcceptableVal." + acceptableDisplayColumn : "CAST(NULL AS VARCHAR(255))";
            string testParamJoin = !string.IsNullOrEmpty(testParamDisplayColumn)
                ? "LEFT OUTER JOIN " + testParamTable + " TestParam ON (TestParam.VA010_TestParameter_ID=QAParam.VA010_TestParameter_ID AND TestParam.IsActive='Y')"
                : "";
            string acceptableJoin = !string.IsNullOrEmpty(acceptableDisplayColumn)
                ? "LEFT OUTER JOIN " + acceptableTable + " AcceptableVal ON (AcceptableVal.VA010_TestPrmtrList_ID=QAParam.VA010_TestPrmtrList_ID AND AcceptableVal.IsActive='Y')"
                : "";

            string holdSql = @"
                SELECT LineConfirm.M_InOutLineConfirm_ID AS GRN_Confirmation_Line_ID,
                       QAParam.VA010_ShipConfParameters_ID AS QA_Record_ID,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Supplier,
                       Product.Name AS Item_Name,
                       COALESCE(QAParam.VA010_QuantityToVerify, LineConfirm.TargetQty, LineConfirm.ConfirmedQty, 0) AS Held_Qty,
                       LineConfirm.Created AS Hold_Started_On,
                       QAParam.M_Product_ID AS QA_Product_ID,
                       QAParam.VA010_QuantityToVerify AS Quantity_To_Verify,
                       QAParam.VA010_TestParameter_ID AS Test_Parameter_ID,
                       " + testParamNameExpr + @" AS Test_Parameter_Name,
                       QAParam.VA010_TestPrmtrList_ID AS Acceptable_Value_ID,
                       " + acceptableNameExpr + @" AS Acceptable_Value_Name,
                       QAParam.VA010_QAQCDate AS QA_QC_Date,
                       QAParam.Remark AS Description,
                       QAParam.VA010_ActualValue AS Actual_Value,
                       LineConfirm.ConfirmationNo AS Confirmation_No,
                       InOutLine.Line AS Line_No,
                       COALESCE(QAProduct.Name, Product.Name) AS Product_Name
                FROM " + lineConfirmTable + @" LineConfirm
                INNER JOIN " + confirmTable + @" Confirm ON (Confirm.M_InOutConfirm_ID=LineConfirm.M_InOutConfirm_ID AND Confirm.IsActive='Y')
                INNER JOIN " + inOutLineTable + @" InOutLine ON (InOutLine.M_InOutLine_ID=LineConfirm.M_InOutLine_ID AND InOutLine.IsActive='Y')
                INNER JOIN " + inOutTable + @" InOut ON (InOut.M_InOut_ID=InOutLine.M_InOut_ID AND InOut.IsActive='Y')
                INNER JOIN " + bPartnerTable + @" BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                INNER JOIN " + productTable + @" Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                LEFT OUTER JOIN " + qaTable + @" QAParam ON (QAParam.M_InOutLineConfirm_ID=LineConfirm.M_InOutLineConfirm_ID AND QAParam.IsActive='Y' AND QAParam.AD_Client_ID=@QA_AD_Client_ID)
                LEFT OUTER JOIN " + productTable + @" QAProduct ON (QAProduct.M_Product_ID=QAParam.M_Product_ID AND QAProduct.IsActive='Y')
                " + testParamJoin + @"
                " + acceptableJoin + @"
                WHERE LineConfirm.IsActive='Y'
                  AND LineConfirm.AD_Client_ID=@AD_Client_ID
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND LineConfirm.VA010_QualCheckMArk='Y'
                  AND (QAParam.VA010_ActualValue IS NULL OR QAParam.VA010_ActualValue='')
                  AND COALESCE(Confirm.Processed,'N')<>'Y'
                  AND COALESCE(Confirm.DocStatus,'DR') IN ('DR','IP')";

            holdSql = MRole.GetDefault(ctx).AddAccessSQL(
                holdSql,
                "LineConfirm",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT HoldData.GRN_Confirmation_Line_ID,
                       HoldData.QA_Record_ID,
                       HoldData.GRN_No,
                       HoldData.Supplier,
                       HoldData.Item_Name,
                       HoldData.Held_Qty,
                       HoldData.Hold_Started_On,
                       HoldData.QA_Product_ID,
                       HoldData.Quantity_To_Verify,
                       HoldData.Test_Parameter_ID,
                       HoldData.Test_Parameter_Name,
                       HoldData.Acceptable_Value_ID,
                       HoldData.Acceptable_Value_Name,
                       HoldData.QA_QC_Date,
                       HoldData.Description,
                       HoldData.Actual_Value,
                       HoldData.Confirmation_No,
                       HoldData.Line_No,
                       HoldData.Product_Name,
                       COUNT(1) OVER () AS TotalRecords
                FROM (
                    " + holdSql + @"
                ) HoldData
                ORDER BY HoldData.Hold_Started_On ASC, HoldData.GRN_No ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@QA_AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            List<object> rows = new List<object>();
            int totalRecords = 0;
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);
                    DateTime? holdStartedOn = Util.GetValueOfDateTime(dr["Hold_Started_On"]);
                    DateTime? qaQcDate = Util.GetValueOfDateTime(dr["QA_QC_Date"]);
                    int testParameterId = Util.GetValueOfInt(dr["Test_Parameter_ID"]);
                    int acceptableValueId = Util.GetValueOfInt(dr["Acceptable_Value_ID"]);
                    string testParameterName = Util.GetValueOfString(dr["Test_Parameter_Name"]);
                    string acceptableValueName = Util.GetValueOfString(dr["Acceptable_Value_Name"]);
                    int lineNo = Util.GetValueOfInt(dr["Line_No"]);

                    rows.Add(new
                    {
                        grnConfirmationLineId = Util.GetValueOfInt(dr["GRN_Confirmation_Line_ID"]),
                        qaRecordId = Util.GetValueOfInt(dr["QA_Record_ID"]),
                        grnNo = Util.GetValueOfString(dr["GRN_No"]),
                        supplier = Util.GetValueOfString(dr["Supplier"]),
                        itemName = Util.GetValueOfString(dr["Item_Name"]),
                        heldQty = Util.GetValueOfDecimal(dr["Held_Qty"]),
                        holdStartedOn = holdStartedOn.HasValue ? holdStartedOn.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : "",
                        qaProductId = Util.GetValueOfInt(dr["QA_Product_ID"]),
                        quantityToVerify = Util.GetValueOfDecimal(dr["Quantity_To_Verify"]),
                        testParameterId = testParameterId,
                        testParameter = !string.IsNullOrEmpty(testParameterName) ? testParameterName : FormatReferenceFallback(testParameterId),
                        acceptableValueId = acceptableValueId,
                        acceptableValue = !string.IsNullOrEmpty(acceptableValueName) ? acceptableValueName : FormatReferenceFallback(acceptableValueId),
                        qaQcDate = qaQcDate.HasValue ? qaQcDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        description = Util.GetValueOfString(dr["Description"]),
                        actualValue = Util.GetValueOfString(dr["Actual_Value"]),
                        confirmationNo = Util.GetValueOfString(dr["Confirmation_No"]),
                        lineNo = lineNo,
                        productName = Util.GetValueOfString(dr["Product_Name"])
                    });
                }

                return Json(JsonConvert.SerializeObject(new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
                }), JsonRequestBehavior.AllowGet);
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

        /// <summary>
        /// Saves the QA actual value, QA/QC date and remark for one QA parameter record.
        /// </summary>
        /// <param name="qaRecordId">VA010_ShipConfParameters_ID.</param>
        /// <param name="actualValue">Working Fine or Not Satisfactory.</param>
        /// <param name="qaQcDate">QA/QC date in yyyy-MM-dd format.</param>
        /// <param name="description">Optional QA note.</param>
        /// <returns>JSON { success, updated } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult SaveQAResult(int qaRecordId = 0, string actualValue = "", string qaQcDate = "", string description = "")
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" });
            }

            string schemaMessage;
            if (!HasRequiredQASchema(out schemaMessage))
            {
                return Json(new { error = schemaMessage });
            }

            if (qaRecordId <= 0)
            {
                return Json(new { error = "QA inspection record is missing." });
            }

            actualValue = (actualValue ?? "").Trim();
            if (actualValue != ActualWorkingFine && actualValue != ActualNotSatisfactory)
            {
                return Json(new { error = "Select a valid QA actual value." });
            }

            object qaDateValue = DBNull.Value;
            if (!string.IsNullOrWhiteSpace(qaQcDate))
            {
                DateTime parsedDate;
                if (!DateTime.TryParseExact(qaQcDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    return Json(new { error = "QA/QC date is not valid." });
                }
                qaDateValue = parsedDate.Date;
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                UPDATE " + GetQATableName("VA010_ShipConfParameters") + @"
                SET VA010_ActualValue=@ActualValue,
                    VA010_QAQCDate=@QAQCDate,
                    Remark=@Description,
                    Updated=CURRENT_DATE,
                    UpdatedBy=@UpdatedBy
                WHERE VA010_ShipConfParameters_ID=@QARecordId
                  AND AD_Client_ID=@AD_Client_ID
                  AND IsActive='Y'";

            SqlParameter[] parameters =
            {
                new SqlParameter("@ActualValue", actualValue),
                new SqlParameter("@QAQCDate", qaDateValue),
                new SqlParameter("@Description", string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description.Trim()),
                new SqlParameter("@UpdatedBy", ctx.GetAD_User_ID()),
                new SqlParameter("@QARecordId", qaRecordId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            try
            {
                if (!CanUpdateQARecord(ctx, qaRecordId))
                {
                    return Json(new { error = "QA inspection record was not found." });
                }

                int updated = DB.ExecuteQuery(sql, parameters);
                if (updated <= 0)
                {
                    return Json(new { error = "QA inspection record was not found." });
                }

                return Json(JsonConvert.SerializeObject(new { success = true, updated = updated }));
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        private bool CanUpdateQARecord(Ctx ctx, int qaRecordId)
        {
            string lineConfirmTable = GetQATableName("M_InOutLineConfirm");
            string confirmTable = GetQATableName("M_InOutConfirm");
            string inOutLineTable = GetQATableName("M_InOutLine");
            string inOutTable = GetQATableName("M_InOut");
            string qaTable = GetQATableName("VA010_ShipConfParameters");

            string sql = @"
                SELECT QAParam.VA010_ShipConfParameters_ID
                FROM " + lineConfirmTable + @" LineConfirm
                INNER JOIN " + qaTable + @" QAParam ON (QAParam.M_InOutLineConfirm_ID=LineConfirm.M_InOutLineConfirm_ID AND QAParam.IsActive='Y')
                INNER JOIN " + confirmTable + @" Confirm ON (Confirm.M_InOutConfirm_ID=LineConfirm.M_InOutConfirm_ID AND Confirm.IsActive='Y')
                INNER JOIN " + inOutLineTable + @" InOutLine ON (InOutLine.M_InOutLine_ID=LineConfirm.M_InOutLine_ID AND InOutLine.IsActive='Y')
                INNER JOIN " + inOutTable + @" InOut ON (InOut.M_InOut_ID=InOutLine.M_InOut_ID AND InOut.IsActive='Y')
                WHERE LineConfirm.IsActive='Y'
                  AND QAParam.VA010_ShipConfParameters_ID=@QARecordId
                  AND QAParam.AD_Client_ID=@AD_Client_ID
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND COALESCE(Confirm.Processed,'N')<>'Y'
                  AND COALESCE(Confirm.DocStatus,'DR') IN ('DR','IP')";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "LineConfirm",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RW
            );

            SqlParameter[] parameters =
            {
                new SqlParameter("@QARecordId", qaRecordId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null)) == qaRecordId;
        }

        private string FormatReferenceFallback(int id)
        {
            return id > 0 ? id.ToString(CultureInfo.InvariantCulture) : "";
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

        private string FindDisplayColumn(string tableName, string[] columnNames)
        {
            for (int i = 0; i < columnNames.Length; i++)
            {
                if (HasColumn(tableName, columnNames[i]))
                {
                    return columnNames[i];
                }
            }

            return "";
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

        private bool HasRequiredQASchema(out string message)
        {
            message = "";

            string lineConfirmTable = GetQATableName("M_InOutLineConfirm");
            string qaTable = GetQATableName("VA010_ShipConfParameters");

            if (!HasTable(lineConfirmTable))
            {
                message = "M_InOutLineConfirm is not available.";
                return false;
            }

            if (!HasTable(qaTable))
            {
                message = "VA010 quality inspection schema is not available.";
                return false;
            }

            string[] lineConfirmColumns =
            {
                "M_InOutLineConfirm_ID",
                "M_InOutConfirm_ID",
                "M_InOutLine_ID",
                "AD_Client_ID",
                "IsActive",
                "Created",
                "ConfirmationNo",
                "TargetQty",
                "ConfirmedQty",
                "VA010_QualCheckMArk"
            };

            string[] qaColumns =
            {
                "VA010_ShipConfParameters_ID",
                "M_InOutLineConfirm_ID",
                "AD_Client_ID",
                "IsActive",
                "M_Product_ID",
                "VA010_QuantityToVerify",
                "VA010_TestParameter_ID",
                "VA010_TestPrmtrList_ID",
                "VA010_QAQCDate",
                "Remark",
                "VA010_ActualValue",
                "Updated",
                "UpdatedBy"
            };

            for (int i = 0; i < lineConfirmColumns.Length; i++)
            {
                if (!HasColumn(lineConfirmTable, lineConfirmColumns[i]))
                {
                    message = "M_InOutLineConfirm." + lineConfirmColumns[i] + " is not available.";
                    return false;
                }
            }

            for (int i = 0; i < qaColumns.Length; i++)
            {
                if (!HasColumn(qaTable, qaColumns[i]))
                {
                    message = "VA010_ShipConfParameters." + qaColumns[i] + " is not available.";
                    return false;
                }
            }

            return true;
        }

        private bool HasTable(string tableName)
        {
            string owner;
            string name;
            SplitTableName(tableName, out owner, out name);

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
                sql = string.IsNullOrEmpty(owner)
                    ? @"
                        SELECT COUNT(1)
                        FROM USER_TABLES
                        WHERE TABLE_NAME=UPPER(@TableName)"
                    : @"
                        SELECT COUNT(1)
                        FROM ALL_TABLES
                        WHERE OWNER=UPPER(@Owner)
                          AND TABLE_NAME=UPPER(@TableName)";
            }

            SqlParameter[] parameters = string.IsNullOrEmpty(owner)
                ? new SqlParameter[] { new SqlParameter("@TableName", name) }
                : new SqlParameter[] { new SqlParameter("@Owner", owner), new SqlParameter("@TableName", name) };
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null)) > 0;
        }

        private bool HasColumn(string tableName, string columnName)
        {
            string owner;
            string name;
            SplitTableName(tableName, out owner, out name);

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
                sql = string.IsNullOrEmpty(owner)
                    ? @"
                        SELECT COUNT(1)
                        FROM USER_TAB_COLUMNS
                        WHERE TABLE_NAME=UPPER(@TableName)
                          AND COLUMN_NAME=UPPER(@ColumnName)"
                    : @"
                        SELECT COUNT(1)
                        FROM ALL_TAB_COLUMNS
                        WHERE OWNER=UPPER(@Owner)
                          AND TABLE_NAME=UPPER(@TableName)
                          AND COLUMN_NAME=UPPER(@ColumnName)";
            }

            SqlParameter[] parameters = string.IsNullOrEmpty(owner)
                ? new SqlParameter[]
                {
                    new SqlParameter("@TableName", name),
                    new SqlParameter("@ColumnName", columnName)
                }
                : new SqlParameter[]
                {
                    new SqlParameter("@Owner", owner),
                    new SqlParameter("@TableName", name),
                    new SqlParameter("@ColumnName", columnName)
                };
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null)) > 0;
        }

        private void SplitTableName(string tableName, out string owner, out string name)
        {
            owner = "";
            name = tableName;

            if (string.IsNullOrEmpty(tableName))
            {
                return;
            }

            int dotIndex = tableName.IndexOf('.');
            if (dotIndex <= 0 || dotIndex >= tableName.Length - 1)
            {
                return;
            }

            owner = tableName.Substring(0, dotIndex);
            name = tableName.Substring(dotIndex + 1);
        }
    }
}
