using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Helpers;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.Security;
using VAdvantage.Classes;
using VAdvantage.Utility;
using VIS.Areas.VIS.Classes;

namespace ViennaAdvantageWeb
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801

    public class MvcApplication : System.Web.HttpApplication
    {
        VISGlobal global = new VISGlobal();
        protected void Application_Start(object sender, EventArgs e)
        {
            AreaRegistration.RegisterAllAreas();
            WebApiConfig.Register(GlobalConfiguration.Configuration);

            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            AuthConfig.RegisterAuth();//test
            global.Application_Start(sender, e);

        }

        protected void Session_Start(object sender, EventArgs e)
        {
            global._session = Session;
            global.Session_Start(sender, e);
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            global.Application_BeginRequest(sender, e);
        }

        protected void Application_AuthenticateRequest(object sender, EventArgs e)
        {
            global.Application_AuthenticateRequest(sender, e);
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            global.Application_Error(sender, e);
        }

        protected void Session_End(object sender, EventArgs e)
        {
            global._session = Session;
            global.Session_End(sender, e);
        }

        protected void Application_End(object sender, EventArgs e)
        {
            global.Application_End(sender, e);
        }


    }
}