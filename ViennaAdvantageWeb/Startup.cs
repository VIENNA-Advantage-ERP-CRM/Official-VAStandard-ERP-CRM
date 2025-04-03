using Microsoft.Owin;
using Owin;
using System;
using System.Threading.Tasks;

[assembly: OwinStartup(typeof(ViennaAdvantageWeb.Startup))]

namespace ViennaAdvantageWeb
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            AuthConfig.RegisterAuth(app);
            // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=316888
        }
    }
}
