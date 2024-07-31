using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.DBase;
//using System.Web.Services;
using System.Collections.Specialized;

namespace VIS.WebPages
{
    public partial class Expire:System.Web.UI.Page
    {
        VLogger log = new VLogger("CreatePassword"); // Log initialization
        protected void Page_Load(object sender, EventArgs e)
        {

            //ViewBag.language = language;

        }
    }
}