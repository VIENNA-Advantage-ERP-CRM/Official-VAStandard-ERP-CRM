using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
    /// Module Name : Material Transfer History Widget (Material Transfer dashboard)
    /// Purpose     : Returns completed / closed stock transfers (DocStatus CO, CL) with
    ///               route (from -> to warehouse), line count and total moved quantity.
    ///               Client side handles search, month/year filter and paging.
    /// ID Prefix   : VAS_176_
    /// </summary>
    public class VAS_176_MaterialTransferHistoryWidgetController : Controller
    {
        private const int MaxRows = 500;

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTransferHistory()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT mm.M_Movement_ID   AS MovementID,
                       mm.DocumentNo      AS DocumentNo,
                       mm.DocStatus       AS DocStatus,
                       mm.MovementDate    AS MovementDate,
                       COALESCE(dt.Name, N'') AS DocTypeName,
                       COALESCE(cu.Name, N'') AS CreatedBy
                FROM M_Movement mm
                LEFT JOIN C_DocType dt ON dt.C_DocType_ID = mm.C_DocType_ID
                LEFT JOIN AD_User cu   ON cu.AD_User_ID = mm.CreatedBy
                WHERE mm.IsActive = 'Y'
                  AND mm.DocStatus IN ('CO', 'CL')
                ORDER BY mm.MovementDate DESC, mm.M_Movement_ID DESC";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "mm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var headers = new List<Header>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read() && headers.Count < MaxRows)
                {
                    headers.Add(new Header
                    {
                        MovementID = Util.GetValueOfInt(dr["MovementID"]),
                        DocumentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        DocStatus = Util.GetValueOfString(dr["DocStatus"]),
                        MovementDate = Util.GetValueOfDateTime(dr["MovementDate"]).GetValueOrDefault().ToString("o"),
                        DocTypeName = Util.GetValueOfString(dr["DocTypeName"]),
                        CreatedBy = Util.GetValueOfString(dr["CreatedBy"])
                    });
                }
                if (dr != null) { dr.Close(); dr.Dispose(); dr = null; }

                var records = new List<object>();
                foreach (var h in headers)
                {
                    string fromWh = "", toWh = "";
                    int lines = 0;
                    decimal qty = 0;
                    string lsql = @"
                        SELECT wSrc.Name AS FromWarehouse, wDst.Name AS ToWarehouse, ml.MovementQty AS Qty
                        FROM M_MovementLine ml
                        LEFT JOIN M_Locator lSrc ON lSrc.M_Locator_ID = ml.M_Locator_ID
                        LEFT JOIN M_Warehouse wSrc ON wSrc.M_Warehouse_ID = lSrc.M_Warehouse_ID
                        LEFT JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                        LEFT JOIN M_Warehouse wDst ON wDst.M_Warehouse_ID = lDst.M_Warehouse_ID
                        WHERE ml.IsActive = 'Y' AND ml.M_Movement_ID = " + h.MovementID + @"
                        ORDER BY ml.Line";
                    IDataReader lr = null;
                    try
                    {
                        lr = DB.ExecuteReader(lsql);
                        while (lr != null && lr.Read())
                        {
                            lines++;
                            qty += Util.GetValueOfDecimal(lr["Qty"]);
                            if (fromWh == "") { fromWh = Util.GetValueOfString(lr["FromWarehouse"]); }
                            if (toWh == "") { toWh = Util.GetValueOfString(lr["ToWarehouse"]); }
                        }
                    }
                    finally { if (lr != null) { lr.Close(); lr.Dispose(); } }

                    records.Add(new
                    {
                        MovementID = h.MovementID,
                        DocumentNo = h.DocumentNo,
                        DocStatus = h.DocStatus,
                        MovementDate = h.MovementDate,
                        DocTypeName = h.DocTypeName,
                        CreatedBy = h.CreatedBy,
                        FromWarehouse = fromWh,
                        ToWarehouse = toWh,
                        LineCount = lines,
                        TotalQty = qty
                    });
                }

                return Json(JsonConvert.SerializeObject(new { records = records }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        private class Header
        {
            public int MovementID;
            public string DocumentNo;
            public string DocStatus;
            public string MovementDate;
            public string DocTypeName;
            public string CreatedBy;
        }
    }
}
