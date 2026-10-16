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
    /// Module Name : Locator Transfer Matrix Widget (Material Transfer dashboard)
    /// Purpose     : Returns completed / closed transfer lines for a month/year, aggregated
    ///               as from-locator x to-locator (quantity and document count).
    /// ID Prefix   : VAS_177_
    /// </summary>
    public class VAS_177_LocatorTransferMatrixWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLocatorMatrix(int month, int year)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // month = 0 means the whole year
            DateTime from = new DateTime(year, month <= 0 ? 1 : month, 1);
            DateTime to = month <= 0 ? from.AddYears(1) : from.AddMonths(1);

            string sql = @"
                SELECT lSrc.Value AS FromLocator,
                       lDst.Value AS ToLocator,
                       ml.MovementQty AS Qty,
                       mm.M_Movement_ID AS MovementID
                FROM M_MovementLine ml
                INNER JOIN M_Movement mm ON mm.M_Movement_ID = ml.M_Movement_ID
                INNER JOIN M_Locator lSrc ON lSrc.M_Locator_ID = ml.M_Locator_ID
                INNER JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                WHERE mm.IsActive = 'Y'
                  AND mm.DocStatus IN ('CO', 'CL')
                  AND mm.MovementDate >= " + DB.TO_DATE(from, true) + @"
                  AND mm.MovementDate < " + DB.TO_DATE(to, true);

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "mm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var cells = new Dictionary<string, Cell>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    string f = Util.GetValueOfString(dr["FromLocator"]);
                    string t = Util.GetValueOfString(dr["ToLocator"]);
                    string key = f + "|" + t;
                    Cell c;
                    if (!cells.TryGetValue(key, out c))
                    {
                        c = new Cell { FromLocator = f, ToLocator = t };
                        cells[key] = c;
                    }
                    c.Qty += Util.GetValueOfDecimal(dr["Qty"]);
                    c.Docs.Add(Util.GetValueOfInt(dr["MovementID"]));
                }

                var list = new List<object>();
                foreach (var c in cells.Values)
                {
                    list.Add(new { FromLocator = c.FromLocator, ToLocator = c.ToLocator, Qty = c.Qty, Docs = c.Docs.Count });
                }

                return Json(JsonConvert.SerializeObject(new { cells = list }), JsonRequestBehavior.AllowGet);
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

        private class Cell
        {
            public string FromLocator;
            public string ToLocator;
            public decimal Qty;
            public HashSet<int> Docs = new HashSet<int>();
        }
    }
}
