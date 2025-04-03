using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VIS.Areas.VIS.Classes;
//using Microsoft.Web.WebPages.OAuth;


namespace ViennaAdvantageWeb
{
    public static class AuthConfig
    {
        public static void RegisterAuth(Owin.IAppBuilder app)
        {
            VISAuthConfig.RegisterAuth(app);
        }

        public static void RegisterAuth()
        {

            ViennaBase.AuthConfig.RegisterAuth();
        }
    }
}
