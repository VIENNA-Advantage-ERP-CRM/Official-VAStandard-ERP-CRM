<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="CreatePassword.aspx.cs" Inherits="VIS.Areas.VIS.WebPages.CreatePassword" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
    <link type="text/css" rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/4.0.0/css/bootstrap.min.css" />
    <link href="../Content/css/font-awesome.min.css" rel="stylesheet" />
    <link href="../..//ViennaBase/Content/fontlib/font-awesome.min.css" rel="stylesheet" />
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
        <div class="VIS-main-container d-flex h-100">
             <div class="VIS-sidebar-wrap">
                <div class="VIS-sidebarInnerWrap">
                    <div class="VIS-SideTextWrap"> 
                        <h2>Welcome to Vienna Advantage</h2>
                        <p>Please create your password to proceed.</p>
                    </div>
                    <div class="VIS-logo-wrap">
                        <img src="../Images/V-logo.png" alt="Logo" />
                    </div>
                    <%--<div class="VIS-poweredBy">
					<small>Powered By:</small>
					<img src="../Content/Images/logo.png" alt="Logo">
				</div>--%>
                </div>
                <!-- CMS02-sidebarInnerWrap -->

            </div>
            <!-- sidebar-wrap -->
            <div class="VIS-content-wrap VIS-middle-align">
                <div class="VIS-confirmation-wrap VIS-setpass-wrap">
                    <div class="VIS-confirm-text-wrap" runat="server" id="divSetPassword">
                        <%--<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_EmailVerified") %>--%>
                        <div >
                                  <h2 runat="server" id="lblHeader"><%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_SetUpPassword") %></h2>
                          </div>
                        <div class="VIS-setpass-form">
                            <div class="VIS-frm-row">
                                <div class="VIS-frm-data">
                                    <label runat="server" id="lblCreatePass"><%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_CreatePassword") %></label>
                                    <sup>*</sup>
                                    <input runat="server" id="txtCreatePass" type="password" autocomplete="new-password" />
                                </div>
                            </div>
                            <div class="VIS-frm-row">
                                <div class="VIS-frm-data">
                                    <label runat="server" id="lblConfirmPass"><%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_ConfirmPassword") %></label>
                                    <sup>*</sup>
                                    <input runat="server" id="txtConfirmPass" type="password" autocomplete="off" />
                                </div>
                            </div>

                            <div class="VIS-frm-btnwrap">
                                <asp:Button class="VIS-submit-btn" ID="btnSave" runat="server" Text="Submit" OnClientClick="return validate()" OnClick="btnSave_Click" />
                            </div>
                            <!-- frm-btnwrap -->
                        </div>
                        <!-- end of form-content -->
                    </div>
                    <div runat="server" id="passwordMsg">
                               <h4> <%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"PasswordReset") %></h4><br/>
                                 <a  href="<%=((HttpRequest)Request)["path"] %>" id="homeLink"><%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_ClickToLongin") %></a>      
                    </div>
                    <div class="VIS-mail-img-wrap">
                        <div class="VIS-mail-img w">
                            <img src="../Content/Images/set-pass.svg" />
                        </div>
                    </div>

                    <label runat="server" id="lblMsg"></label>
                    <!-- end of form-wrap -->
                </div>
            </div>
        </div>
    </form>
    <script src="https://ajax.googleapis.com/ajax/libs/jquery/3.7.1/jquery.min.js"></script>
    <script>
    var $body = $('body');
 
        function AD() {
 
            var _overLay = $('<div id="overlayMsgDialog" class="web_dialog_overlay"></div>');
 
            var $mainDivParent = $('<div class="vis-PopupWrap-alertmain" tabIndex=1>').focus();
            //<div class="vis-confirm-popup-check"><input type="checkbox"><label>Background</label></div>
            var $mainDiv = $('<div id="VAPOS_ErrorInfo" class="vis-PopupWrap-alert">' +
                '<input class="vis-Dialog-buttons-text" type="number"  tabindex="-30" style="z-index:-44;position:absolute"  autofocus="autofocus"  > ' +
                '       <div class="vis-popup-headerContainer">                                           ' +
                '       <div class="vis-PopupHeader-alert">' +
                '           <h4 id="VAPOS_CLInformation">Information</h4>' +
                '           <span id="btnCloseInfo" class="fa fa-times"></span>                     ' +
                '       </div>  </div>                                                                                   ' +
                '       <div class="vis-PopupContent-alert">                                                     ' +
 
                '               <div class="form-group vis-PopupInput-alert" style="width: 100%">                 ' +
                '               <img class="vis-alert-img" style="float:left"  />                                                   ' +
                '                   <label style="width: 90%;padding-left: 10px;word-break: break-word" id="VAPOS_lblErrorInfo"></label>                  ' +
                '                 <div class="vis-confirm-customUI" style="display:none"> </div>         ' +
                '               </div>                                                                           ' +
                '                     <div class="vis-Dialog-buttons" style="display:none">                                           ' +
                '<input class="vis-Dialog-buttons-OK " type="button" value="" > <input  class="vis-Dialog-buttons-Cancel"  type="button" value="" >' +
                '                                               </div>                                           ' +
                '                                                                                                ' +
 
                '       </div>                                                                                   ' +
                '   </div>');
 
            $mainDivParent.append($mainDiv);
 
            $body.append(_overLay).append($mainDivParent);
 
            var _hideOverlay = true;
            var _header = $mainDiv.find(".vis-PopupHeader-alert");
 
            var _headerContainer = $mainDiv.find('.vis-popup-headerContainer');
 
            var _btnCloseInfo = $mainDiv.find("#btnCloseInfo"); //btn close
 
            var _headerText = $mainDiv.find("#VAPOS_CLInformation"); //headertext
            var _headerImg = $mainDiv.find(".vis-alert-img"); //header img
 
            var _content = $mainDiv.find(".vis-PopupContent-alert");
 
            var _contentMsg = $mainDiv.find("#VAPOS_lblErrorInfo"); //label
 
            var _customUI = $mainDiv.find('.vis-confirm-customUI');
 
            //  var _overLay = _main.find("#overlayMsgDialog");
            var _busyInd = $mainDiv.find("#VAPOS_busyInd");
 
            var _btnOK = null;
            var _btnCancel = null;
            //var _txtBx = $mainDiv.find(".vis-Dialog-buttons-text");;
 
            var _callback = null;
 
            _btnCloseInfo.on("click", function (e) {
                closeIt(e);
            });
 
            function handleKeys(askUser) {
                if (askUser) {
                    window.setTimeout(function () {
                        _btnCancel.focus();
                        $mainDivParent.on("keydown", function (e) {
                            if (e.keyCode == 9) {
                                if (_btnOK.is(':focus')) {
                                    _btnCancel.focus();
                                }
                                else if (_btnCancel.is(':focus')) {
                                    _btnOK.focus();
                                }
                                else {
                                    _btnCancel.focus();
                                }
                                e.preventDefault();
                                e.stopPropagation();
                                //return false;
                            }
                            else if (e.keyCode == 27) {
                                e.preventDefault();
                                e.stopPropagation();
                                closeIt(e);
                                //  _txtBx.off("keydown");
                                return false;
                            }
                        });
                    }, 100);
                }
                else {
                    $mainDivParent.focus();
                    $mainDivParent.on("keydown", function (e) {
                        e.preventDefault();
                        e.stopPropagation();
                        closeIt(e);
                        //  _txtBx.off("keydown");
                        return false;
                    });
                }
            };
 
 
            function closeIt(e) {
                e.stopPropagation();
 
                if (_callback)
                    _callback();
                _callback = null;
 
                disposeEvents();
            };
 
 
            function disposeEvents() {
                _overLay.hide();
 
                $mainDivParent.css({ "position": "inherit", "display": "none" });
                $mainDiv.find('.vis-Dialog-buttons').css("display", "none");
                //$mainDiv.fadeOut(300);
 
                if (_btnOK) {
                    _btnOK.off("click");
                }
 
                if (_btnCancel) {
                    _btnCancel.off("click");
                }
 
                $mainDivParent.off("keydown");
 
                //$mainDivParent.remove();
                //$mainDiv.remove();
                //$mainDivParent = null;
                //$mainDiv = null;
                //_btnOK.remove();
                //_btnCancel.remove();
                //_btnCancel = null;
                //_btnOK = null;
            };
 
 
            function info(msg, header, callback) {
                _callback = callback;
                try {
                    // $('#prodError')[0].play();
                }
                catch (ex) {
                }
                //$mainDiv.find('.vis-Dialog-buttons').css("display", "none");
                $mainDivParent.css({ "position": "absolute", "display": "flex" });
                $mainDiv.show();
                //_btnCloseInfo.removeClass();
                //_btnCloseInfo.addClass("vis-alert-close vis-alert-close-info");
                _header.removeClass();
                _header.addClass("vis-PopupHeader-alert vis-PopupHeader-alert-info");
                _content.removeClass();
                _content.addClass("vis-PopupContent-alert vis-PopupContent-alert-info");
                _headerContainer.removeClass();
                _headerContainer.addClass("vis-popup-headerContainer vis-popup-headerContainer-info");
                _headerImg.attr("src", "../Images/base/info-icon.png");
 
                _prodFound = true;
                _contentMsg.text(msg);
                if (!header) {
                    _headerText.text("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"Info") %>");
                }
 
                //_hideOverlay = hideOverlay;
                _overLay.show();
                $mainDiv.fadeIn(300);
                handleKeys(false);
                //_txtBx.on("keydown", function (e) {
                //    clss(e);
                //});
 
                //_txtBx.on("focusin", function (e) {
                //    e.stopPropagation();
                //});
 
                //    _txtBx.focus();
 
            };
 
 
            function clss(e) {
                if (e.keyCode == 27) {
                    e.preventDefault();
                    e.stopPropagation();
                    closeIt(e);
                    //  _txtBx.off("keydown");
                    return false;
                }
            };
 
            function ask(msg, header, callback) {
                _callback = callback;
                try {
                    // $('#prodError')[0].play();
                }
                catch (ex) {
                }
                $mainDivParent.css({ "position": "absolute", "display": "flex" });
                $mainDiv.show();
                //_btnCloseInfo.removeClass();
                //_btnCloseInfo.addClass("vis-alert-close vis-alert-close-info");
                _header.removeClass();
                _header.addClass("vis-PopupHeader-alert vis-PopupHeader-alert-info");
                _content.removeClass();
                _content.addClass("vis-PopupContent-alert vis-PopupContent-alert-info vis-PopupContent-alert-Confirm");
                _headerContainer.removeClass();
                _headerContainer.addClass("vis-popup-headerContainer vis-popup-headerContainer-info");
 
                _headerImg.attr("src", "../Images/base/confirm-icon.png");
 
                var $btnsDiv = $mainDiv.find('.vis-Dialog-buttons');
 
                $btnsDiv.css("display", "inherit");
 
                _btnOK = $btnsDiv.find(".vis-Dialog-buttons-OK");
                _btnCancel = $btnsDiv.find(".vis-Dialog-buttons-Cancel");
 
                _prodFound = true;
                _contentMsg.text(msg);
                if (!header) {
                    _headerText.text("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"Confirm") %>");
                }
                //_hideOverlay = hideOverlay;
                _overLay.show();
                $mainDiv.fadeIn(300);
 
                _btnOK.one("click", function () {
                    disposeEvents();
                    $btnsDiv.css("display", "none");
                    _callback(true);
                    // _btnCloseInfo.trigger("click");
 
                });
 
                _btnCancel.one("click", function () {
                    disposeEvents();
                    $btnsDiv.css("display", "none");
                    _callback(false);
                    // _btnCloseInfo.trigger("click");
 
                });
                handleKeys(true);
                //_txtBx.on("keydown", function (e) {
                //    clss(e);
                //});
 
                //_txtBx.on("focusin", function (e) {
                //    e.stopPropagation();
                //});
 
                //_txtBx.focus();
 
            };
 
            function askCustomUI(msg, header, $rootDiv, callback) {
                _callback = callback;
                try {
                    // $('#prodError')[0].play();
                }
                catch (ex) {
                }
                $mainDivParent.css({ "position": "absolute", "display": "flex" });
                $mainDiv.show();
                //_btnCloseInfo.removeClass();
                //_btnCloseInfo.addClass("vis-alert-close vis-alert-close-info");
                _header.removeClass();
                _header.addClass("vis-PopupHeader-alert vis-PopupHeader-alert-info");
                _content.removeClass();
                _content.addClass("vis-PopupContent-alert vis-PopupContent-alert-info vis-PopupContent-alert-Confirm");
                _headerContainer.removeClass();
                _headerContainer.addClass("vis-popup-headerContainer vis-popup-headerContainer-info");
 
                _headerImg.attr("src", "../Images/base/confirm-icon.png");
 
                var $btnsDiv = $mainDiv.find('.vis-Dialog-buttons');
 
                $btnsDiv.css("display", "inherit");
 
                _btnOK = $btnsDiv.find(".vis-Dialog-buttons-OK");
                _btnCancel = $btnsDiv.find(".vis-Dialog-buttons-Cancel");
 
                _prodFound = true;
                _contentMsg.text(msg);
                _customUI.empty();
                _customUI.css('display', 'block');
                _customUI.append($rootDiv);
                if (!header) {
                    _headerText.text("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"Confirm") %>");
                }
                //_hideOverlay = hideOverlay;
                _overLay.show();
                $mainDiv.fadeIn(300);
 
                _btnOK.one("click", function () {
                    disposeEvents();
                    $btnsDiv.css("display", "none");
                    _customUI.css('display', 'none');
                    _customUI.empty();
                    _callback(true);
                    // _btnCloseInfo.trigger("click");
 
                });
 
                _btnCancel.one("click", function () {
                    disposeEvents();
                    $btnsDiv.css("display", "none");
                    _customUI.css('display', 'none');
                    _customUI.empty();
                    _callback(false);
                    // _btnCloseInfo.trigger("click");
 
                });
                handleKeys(true);
                //_txtBx.on("keydown", function (e) {
                //    clss(e);
                //});
 
                //_txtBx.on("focusin", function (e) {
                //    e.stopPropagation();
                //});
 
                //_txtBx.focus();
 
            };
 
            function warn(msg, header, callback) {
                _callback = callback;
                try {
                    //$prodBuzzer[0].play();
                }
                catch (ex) {
                }
                //$mainDiv.find('.vis-Dialog-buttons').css("display", "none");
                _prodFound = true;
                $mainDivParent.css({ "position": "absolute", "display": "flex" });
                $mainDiv.show();
                //_btnCloseInfo.removeClass();
                //_btnCloseInfo.addClass("vis-alert-close vis-alert-close-warn");
                _header.removeClass();
                _header.addClass("vis-PopupHeader-alert vis-PopupHeader-alert-warn");
                _content.removeClass();
                _content.addClass("vis-PopupContent-alert vis-PopupContent-alert-warn");
 
                _headerContainer.removeClass();
                _headerContainer.addClass("vis-popup-headerContainer vis-popup-headerContainer-warn");
                _headerImg.attr("src","../Images/base/warning-icon.png");
 
 
                _contentMsg.text(msg);
                // _hideOverlay = hideOverlay;
                if (!header) {
                    _headerText.text("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"Warning") %>");
                }

 
                _overLay.show();
                handleKeys(false);
                //_txtBx.on("keydown", function (e) {
                //    clss(e);
                //});
 
                //_txtBx.on("focusin", function (e) {
                //    e.stopPropagation();
                //});
 
                //_txtBx.focus();
 
 
 
                // _busyInd.hide();
            };
 
            function error(msg, header, callback) {
                _callback = callback;
                try {
                    //$prodBuzzer[0].play();
                }
                catch (ex) {
                }
                //$btnsDiv.css("display", "none");
                _prodFound = true;
                $mainDivParent.css({ "position": "absolute", "display": "flex" });
                $mainDiv.show();
                //_btnCloseInfo.removeClass();
                //_btnCloseInfo.addClass("vis-alert-close vis-alert-close-error");
                _header.removeClass();
                _header.addClass("vis-PopupHeader-alert vis-PopupHeader-alert-error");
                _content.removeClass();
                _content.addClass("vis-PopupContent-alert vis-PopupContent-alert-error");
                _headerContainer.removeClass();
                _headerContainer.addClass("vis-popup-headerContainer vis-popup-headerContainer-error");
                _headerImg.attr("src", "../Images/base/error-icon.png");
 
                _contentMsg.text(msg);
                // _hideOverlay = hideOverlay;
                if (!header) {
                    _headerText.text("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"Error") %>");
                }
 
                _overLay.show();
                $mainDiv.fadeIn(300);
                handleKeys(false);
                //_txtBx.on("keydown", function (e) {
                //    clss(e);
                //});
 
                //_txtBx.on("focusin", function (e) {
                //    e.stopPropagation();
                //});
 
                //_txtBx.focus();
 
 
                //_busyInd.hide();
            };
 
            return {
                info: info,
                ask: ask,
                error: error,
                warn: warn,
                askCustomUI: askCustomUI
            }
 
        };
    </script>
    <script>       
        function validate() {
            var txtCreatePass = document.getElementById("txtCreatePass");
            if (txtCreatePass.value.length == 0) {
                AD().info("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_CreatePassValidation") %>");
                <%--alert("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_CreatePassValidation") %>");--%>
                txtCreatePass.focus();
                return false;
            } else {                
                var regex = /^(?=.*?[A-Z])(?=.*?[a-z])(?=.*?[0-9])(?=.*?[^\w\s]).{5,}$/
                var isValidPattern = regex.test(txtCreatePass.value);
                if (!isValidPattern) {
                 AD().info("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"mustMatchCriteria") %>");
                    <%--alert("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"mustMatchCriteria") %>");--%>                    
                    txtCreatePass.focus();
                    return false;
                }
            }          
            

            var txtConfirmPass = document.getElementById("txtConfirmPass");
            if (txtConfirmPass.value == 0) {
                AD().info("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_ConfirmPassValidation") %>");
               <%-- alert("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_ConfirmPassValidation") %>");--%>
                txtConfirmPass.focus();
                return false;
            }

            if (txtCreatePass.value != txtConfirmPass.value) {
                AD().info("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_ConfirmPassValidation") %>");
                <%--alert("<%= VAdvantage.Utility.Msg.GetMsg(((HttpRequest)Request)["lang"],"VIS_samePassword") %>");--%>
                txtConfirmPass.focus();
                return false;
            }
        }
    </script>
</body>
</html>
