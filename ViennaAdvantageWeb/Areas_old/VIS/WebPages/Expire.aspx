<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Expire.aspx.cs" Inherits="VIS.WebPages.Expire" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
    <link type="text/css" rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/4.0.0/css/bootstrap.min.css" />
    <link type="text/css" rel="stylesheet" href="../Content/VIS.all.min.css" />
    <meta name="description" content="ERP " />
    <meta name="author" content="Vienna" />
    <meta http-equiv="cache-control" content="max-age=0" />
    <meta http-equiv="cache-control" content="no-cache" />
    <meta http-equiv="expires" content="0" />
    <meta http-equiv="expires" content="Tue, 01 Jan 1980 1:00:00 GMT" />
    <meta http-equiv="pragma" content="no-cache" />

    <link href="~/favicon.ico" rel="shortcut icon" type="image/x-icon" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0, user-scalable=0, minimum-scale=1.0, maximum-scale=1.0" />
</head>
<body>
    <form id="form1" runat="server">
    <div class="VIS-main-container">
       <div class="VIS-sidebar-wrap">
                <div class="VIS-sidebarBgImg"></div>
                <div class="VIS-sidebarInnerWrap">
                    <div class="VIS-logo-wrap">
                        <img src="../Images/V-logo.png" alt="Logo" />
                    </div>
                    <div class="VIS-SideTextWrap">                        
                    </div>
                  <%-- <div class="VIS-poweredBy">
					<small>Powered By:</small>
					<img src="../Content/Images/logo.png" alt="Logo">
				</div>--%>
                </div>
                <!-- CMS02-sidebarInnerWrap -->

            </div>
        <!-- sidebar-wrap -->
        <div class="VIS-content-wrap VIS-middle-align">
            <div class="VIS-confirmation-wrap">
                <div class="VIS-confirm-text-wrap" runat="server" id="divExpire">     
                    <h2 runat="server" id="lblHeader"> <%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_LinkExpired") %></h2>
                    <p><%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"LinkExpired") %></p>
                </div>
               
                
                <!-- confirm-text-wrap -->
                <div class="VIS-mail-img-wrap">
                    <div class="VIS-mail-img">
                        <img src="../Content/Images/mail.svg"/></div>
                </div>
            </div>
            <!-- confirmation-wrap -->
        </div>
        <!-- content-wrap -->
    </div>
        </form>
    <!-- main-container -->
</body>
</html>