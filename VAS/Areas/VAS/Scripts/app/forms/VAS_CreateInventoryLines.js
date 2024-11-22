; VAS = window.VAS || {};
; (function (VAS, $) {
    VAS.VAS_CreateInventoryLines = function () {

        this.frame;
        this.windowNo;
        this.Record_ID;
        this.Table_ID;
        var $CartHeaderSection;
        var $CartLineDetailSection;
        var $self = this; //scoped self pointer
        var $root = $('<div style="overflow:hidden;height:100%;width:100%;">');
        var InputCartSearch = null;
        var InputRefSearch = null;
        var CartName = null;
        var RefNo = null;
        var ClearFilter = null;
        var $FromDate = null;
        var $ToDate = null;
        var FromDatediv = null;
        var toDatediv = null;
        var userid = [];
        var UserIds = "";
        var selectedLines = [];
        var IsUpdateTrue = false;
        var window_ID = 0;
        var AD_tab_ID = 0;
        var Reference = null;
        var WindowName = "";
        var _FromLocatorLookUp;
        var $FromLocatorControl;
        var _FlocatCtrl = "";
        var FromLoctr = 0;
        var ToLoctor = 0;

        var _ToLocatorLookUp;
        var ToWarehouse = 0;
        var DTDSrcWarehouse = 0;
        var Bpartner = 0;
        var OrderNo = 0;
        //var WindowName = VIS.context.m_map[1]["1|0|Name"];

        this.initalize = function () {
            createBusyIndicator();
            $CartHeaderSection = $('<div class="VAS-filters-container" id="VAS-CartHeader_' + $self.windowNo + '">');
            $CartLineDetailSection = $('<div class="VAS-flyout-body h-100" id="VAS-CartLines_' + $self.windowNo + '" style="display:none;">');

            //VIS0336-Cart detail and left filter
            $CartHeaderSection.append('<div class="VAS-left-filters VAS-CartDetail">'
                + '<div class="VAS-field-opt-col">'
                + '<div class="VAS-filters-heading">' + VIS.Msg.getMsg("VAS_Filters") + '</div>'
                + '<div class="VAS-form-field">'
                + '<label for="">' + VIS.Msg.getMsg("VAS_CreatedBy") + '</label>'
                + '<div class="VAS-input-w-icon" id="VAS-UserId_' + $self.windowNo + '">'
                + '<i class="fa fa-search" aria-hidden="true"></i>'
                + '<input type="text" id="VAS_UserSearch_' + $self.windowNo + '" placeholder="Search user">'
                + '</div>'
                + '<div class="VAS-lblChip-container VAS_UserTag">'
                + '</div>'
                + '</div>'
                + '<div class="VAS-input-w-icon">'
                + ' <i class="fa fa-search" aria-hidden="true"></i>'
                + '<input type="text" id="VAS-Refnumber_' + $self.windowNo + '" placeholder="Reference No.">'
                + '</div>'

                + '<div class="VAS-form-field">'
                + '<label for="">' + VIS.Msg.getMsg("VAS_DateRange") + '</label>'
                + '<div class="VAS-Datefileter">'
                + '<div class="vas-FromdateDiv w-100">'
                + '</div>'
                + '<div class="vas-todateDiv w-100">'
                + '</div>'
                + '</div>'
                + '</div>'

                + '</div>'
                + '<div class="VAS-filter-btn">'
                + '<input type="button" class="btn ui-button-outline ui-widget" id= "VAS-ClearFilter_' + $self.windowNo + '" value="Clear Filters">'
                + '</div>'
                + '</div>'



                + '<div class= "VAS-avail-cart-col" >'
                + '<div class="VAS-cartCountW-input">'
                + '<label for=""> ' + VIS.Msg.getMsg("VAS_AvailCarts") + '<span class="VAS-Availcarts" id= "VAS-Availcarts_' + $self.windowNo + '"> ' + 0 + '<span></label>'
                + '<div class="VAS-input-w-icon">'
                + ' <i class="fa fa-search" aria-hidden="true"></i>'
                + '<input type="text" id="VAS-CartSearch_' + $self.windowNo + '" placeholder="Search Cart">'
                + '</div>'
                + '</div>'

                + '<div class= "VAS-carts-results-list">'
                + '<div class="VAS-carts-box-col" id="InventoryCart_' + $self.windowNo + '">'

                + '</div>'
                + '</div>'

                + '</div>');
            //VIS0336-Cart lines
            $CartLineDetailSection.append('<div class="VAS-carts-listing-col VAS-CartLineDetails h-100">'
                + '<div class="VAS-cart-list-header">'
                + '<a><span class="vis vis-back-arrow" id="VAS-BackArrow_' + $self.windowNo + '"></span></a>'
                + '<div class="VAS-space-between">'
                + '<div class="VAS-heading-w-lbl-col">'
                + '<div class="VAS-createName-heading"><span class="VAS-CartName" id= "VAS-CartName_' + $self.windowNo + '"><span></div>'
                + '<div class="VAS-cartAct-lbl">'
                + '<span class="VAS-TransactionType" id= "VAS-TransactionType_' + $self.windowNo + '"><span>'

                + '</div>'
                + '</div>'

                + '<div class="VAS-Inventorymove" style="display:flex;gap:10px;width:50%;">'
                + ' <div class="input-group vis-input-wrap vis-formouterwrpdiv" id="VAS_FlocatorCtrl' + $self.windowNo + '" style="width:50%" > '
                + '</div > '

                + ' <div class="input-group vis-input-wrap vis-formouterwrpdiv" id="VAS_TolocatorCtrl' + $self.windowNo + '" style="width:50%" > '
                + '</div > '
                + '</div > '



                + '<div class="VAS-total-lines">'
                + '<span class="VAS-lineCount"><span class= "VAS-LineCount" id = "VAS-LineCount_' + $self.windowNo + '"><span></span>'
                + '<span class="VAS-line-lbl">Line</span>'
                + '</div>'
                + '</div>'
                + '</div>'

                + '<div class="VAS-hgt-spaceBetween VAS-LineDetails">'
                + '<div class="table-responsive VAS-filter-list-table-col">'
                + '<table class="table">'
                + '<thead>'
                + '<tr>'
                + '<th>'
                + '<div>'
                + '<input type="checkbox" id="selectAllCheckbox" value="option1" aria-label="...">'
                + '</div>'
                + '</th>'
                + '<th>' + VIS.Msg.getMsg("VAS_Code") + '</th>'
                + '<th>' + VIS.Msg.getMsg("VAS_Product") + '</th>'
                + '<th>' + VIS.Msg.getMsg("VAS_Attribute") + '</th>'
                + '<th>' + VIS.Msg.getMsg("VAS_UOM") + '</th>'
                + '<th class="text-right">' + VIS.Msg.getMsg("VAS_Qty") + '</th>'
                + '</tr>'
                + '</thead>'
                + '<tbody  id="VAS-CartLinesDetails_' + $self.windowNo + '">'


                + '</tbody>'
                + '</table>'
                + '</div>'

                + '<div class="VAS-footerBtn-w-opt">'
                + '<div class="form-check VAS-Update" style="display:none;">'
                + '<input  type="checkbox" value="" id="VAS-Updatedcheckbox_' + $self.windowNo + '">'
                + '<label class="form-check-label" for="defaultCheck8">'
                + 'Update'
                + '</label>'
                + '</div>'
                + '<input type="button" class="btn ui-button ui-widget" id="VAS-CreateLines_' + $self.windowNo + '" value="Create Lines">'
                + '</div>'
                + ' </div>'

                + '</div>'
                + '</div>');
            $root.append($CartHeaderSection).append($CartLineDetailSection);

            AD_tab_ID = VIS.context.getWindowTabContext($self.windowNo, 0, "AD_Tab_ID");
            window_ID = VIS.dataContext.getJSONRecord("InfoProduct/GetWindowID", AD_tab_ID.toString());
            WindowName = VIS.context.getContext($self.windowNo, "ScreenName");
            ToWarehouse = VIS.context.getContextAsInt($self.windowNo, "M_Warehouse_ID");
            DTDSrcWarehouse = VIS.context.getContextAsInt($self.windowNo, "DTD001_MWarehouseSource_ID");
            Bpartner = VIS.context.getContextAsInt($self.windowNo, "C_BPartner_ID");
            OrderNo = VIS.context.getContextAsInt($self.windowNo, "C_Order_ID");

            if (window_ID == "168") {//update record only allowed in case of physical inventory
                $root.find(".VAS-Update").css("display", "block");
            }
        };

        this.intialLoad = function () {
            _User = $root.find("#VAS_UserSearch_" + $self.windowNo);
            ClearFilter = $root.find('#VAS-ClearFilter_' + $self.windowNo);
            FromDatediv = $root.find('.vas-FromdateDiv');
            toDatediv = $root.find('.vas-todateDiv');
            Userdiv = $root.find('#VAS-UserId_' + $self.windowNo);
            Userinput = $root.find('#VAS_UserSearch_' + $self.windowNo);
            InputCartSearch = $root.find('#VAS-CartSearch_' + $self.windowNo);
            InputRefSearch = $root.find('#VAS-Refnumber_' + $self.windowNo);

            /* autocompletion on user input for loading user*/
            Userinput.autocomplete({
                classes: {
                    'ui-autocomplete': 'VAS-AutoComp'
                },
                minLength: 0,
                source: function (request, response) {
                    if (request.term.trim().length == 0) {
                        return;
                    }
                    GetUsers(response, request.term)
                },
                select: function (ev, ui) {

                    tag = $('<div class="VAS-lbl-chip" item-id="' + ui.item.ids + '">'
                        + '<span class= "VAS-chipTxt">' + ui.item.label + '</span><span class="vis vis-cross"></span>'
                        + '</div>');

                    tag.find('.vis-cross').on('click', function () {
                        itemId = $(this).parent().attr('item-id');
                        $(this).parent().remove();
                        userid = userid.filter(item => item !== parseInt(itemId));
                        UserIds = userid.join(',');
                        LoadCartData();
                    });

                    userid.push(ui.item.ids);
                    UserIds = userid.join(',');
                    LoadCartData();
                    if (!isProductTagExist(ui.item.ids)) {
                        this.value = "";
                        return true;
                    }

                    if (ui.item.ids != 9999) {
                        itemId = $(this).parent().attr('item-id');
                        $root.find('.VAS_UserTag').append(tag);
                        this.value = "";
                        return false;
                    }
                },

            })

            //VIS0336-Date fileters
            $FromDatewrapDiv = $('<div class="input-group vis-input-wrap">');
            $FromDate = new VIS.Controls.VDate("DateReport", true, false, true, VIS.DisplayType.Date, "DateReport");
            var $FromDateWrap = $('<div class="vis-control-wrap">');
            $FromDatewrapDiv.append($FromDateWrap);
            $FromDateWrap.append($FromDate.getControl().attr('placeholder', ' ').attr('data-placeholder', ''));
            FromDatediv.append($FromDatewrapDiv);

            $toDatewrapDiv = $('<div class="input-group vis-input-wrap">');
            $ToDate = new VIS.Controls.VDate("DateReport", true, false, true, VIS.DisplayType.Date, "DateReport");
            var $toDateWrap = $('<div class="vis-control-wrap">');
            $toDatewrapDiv.append($toDateWrap);
            $toDateWrap.append($ToDate.getControl().attr('placeholder', ' ').attr('data-placeholder', ''));
            toDatediv.append($toDatewrapDiv);


            LoadCartData();
            InitEvents();
        }
        //VIS0336-method for loading the cart names and on click of arrow lines will load
        function LoadCartData() {
            $self.setBusy(true);
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "InventoryLines/GetIventoryCartData",
                { "CartName": CartName, "UserId": UserIds, "FromDate": $FromDate.getValue(), "ToDate": $ToDate.getValue(), "RefNo": RefNo, "windowID": window_ID, "RecordId": $self.Record_ID, "WindowName": WindowName, "ToWarehouse": ToWarehouse, "DTDSrcWarehouse": DTDSrcWarehouse, "BPartnerId": Bpartner, "OrderNo": OrderNo }, function (data) {

                    $root.find("#VAS-CartLines_" + $self.windowNo).css("display", "none");
                    $root.find("#VAS-CartHeader_" + $self.windowNo).css("display", "");
                    $root.find("#InventoryCart_" + $self.windowNo).empty();
                    $root.find("#VAS-Availcarts_" + $self.windowNo).text('(' + 0 + ')');

                    if (data && data.length > 0) {
                        for (var i = 0; i < data.length; i++) {
                            $root.find("#VAS-Availcarts_" + $self.windowNo).text('(' + data.length + ')');
                            $root.find("#InventoryCart_" + $self.windowNo).append('<div class= "VAS-cart-box">'
                                + '<div class="VAS-cartName-w-des">'
                                + '<h1>' + data[i].CartName + '</h1>'
                                + '<div class="VAS-cart-type">Type: ' + data[i].TransactionType + '</div>'
                                + '<div class="VAS-cart-created">Created by: ' + data[i].CreatedBy + '</div>'
                                + '</div>'
                                + '<div class="VAS-linesdetail-col">'
                                + '<div class="VAS-total-lines">'
                                + '<span class="VAS-lineCount">' + data[i].CartLineCount + '</span>'
                                + '<span class="VAS-line-lbl">Line</span>'
                                + '</div>'
                                + '<a href="javascript:void(0);"><i class="fa fa-caret-right" id="VAS-CartId_' + $self.windowNo + '" vas-cart-id = "' + data[i].CartId + '"  vas-cart-name = "' + data[i].CartName + '"  vas-transactiontype = "' + data[i].TransactionType + '"  vas-createdby = "' + data[i].CreatedBy + '" vas-CartRef = "' + data[i].ReferenceNo + '" aria-hidden="true" ></i></a> '
                                + '</div>'
                                + '</div>');

                        }

                        $root.find("#VAS-CartId_" + $self.windowNo).off('click');
                        $root.on('click', "#VAS-CartId_" + $self.windowNo, function () {
                            $root.find("#VAS-CartLines_" + $self.windowNo).css("display", "block");
                            $root.find("#VAS-CartHeader_" + $self.windowNo).css("display", "none");

                            var CartId = $(this).attr('vas-cart-id');
                            Reference = $(this).attr('vas-CartRef');

                            var result = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "InventoryLines/GetIventoryCartLines", { "CartId": CartId, "RefNo": Reference, "ScreenName": WindowName, "RecordId": $self.Record_ID });

                            if (result && result.length > 0) {
                                $root.find("#VAS-CartName_" + $self.windowNo).text($(this).attr('vas-cart-name'));
                                $root.find("#VAS-TransactionType_" + $self.windowNo).text($(this).attr('vas-transactiontype') + " | Created by:" + $(this).attr('vas-createdby'));
                                $root.find("#VAS-LineCount_" + $self.windowNo).text(0);
                                $root.find("#VAS-CartLinesDetails_" + $self.windowNo).empty();
                                for (var i = 0; i < result.length; i++) {
                                    $root.find("#VAS-LineCount_" + $self.windowNo).text(result.length);

                                    $root.find("#VAS-CartLinesDetails_" + $self.windowNo).append('<tr cart-code="' + result[i].Code + '" product-id="' + result[i].ProductId +
                                        '" attr-id="' + result[i].AttrId + '"uom-id="' + result[i].UomId + '" inventorycount-id="' + result[i].InventoryCountId + '" qty="' + result[i].Quantity + '">'
                                        + '<td>'
                                        + '<div>'
                                        + '<input class="lineCheckbox "type="checkbox"  value="option1" aria-label="...">'
                                        + '</div>'
                                        + '</td>'
                                        + '<td>' + result[i].Code + '</td>'
                                        + '<td>' + result[i].ProductName + '</td>'
                                        + '<td>' + result[i].AttrName + '</td>'
                                        + '<td>' + result[i].UomName + '</td>'
                                        + '<td class="text-right">' + result[i].Quantity + '</td>'
                                        + '</tr>');
                                }

                                $root.find("#VAS-CartLinesDetails_" + $self.windowNo).on('change', '.lineCheckbox', function () {
                                    var allChecked = $root.find("#VAS-CartLinesDetails_" + $self.windowNo).find(".lineCheckbox").length === $root.find("#VAS-CartLinesDetails_" + $self.windowNo).find(".lineCheckbox:checked").length;
                                    $root.find("#selectAllCheckbox").prop('checked', allChecked);
                                    Fetchdata();
                                });


                                if (WindowName == "VAS_InventoryMove") {

                                    $root.find(".VAS-Inventorymove").css("display", "flex");
                                    $root.find("#VAS_FlocatorCtrl" + $self.windowNo).empty();
                                    $root.find("#VAS_TolocatorCtrl" + $self.windowNo).empty();

                                    _FlocatCtrl = $root.find("#VAS_FlocatorCtrl" + $self.windowNo);
                                    _TolocatCtrl = $root.find("#VAS_TolocatorCtrl" + $self.windowNo);

                                    //From warehouse
                                    var SqlWhere = " M_Locator.M_Warehouse_ID=" + DTDSrcWarehouse;
                                    _FromLocatorLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "M_Locator_ID", 0, false, SqlWhere);
                                    $FromLocatorControl = new VIS.Controls.VComboBox("M_Locator_ID", false, false, true, _FromLocatorLookUp, 50);
                                    var Flocatorctrlwrap = $('<div class="vis-control-wrap">');
                                    var Flocatorbtnwrap = $('<div class="input-group-append">');
                                    _FlocatCtrl.append(Flocatorctrlwrap);
                                    _FlocatCtrl.append(Flocatorbtnwrap);
                                    Flocatorctrlwrap.append($FromLocatorControl.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append($('<label>' + VIS.Msg.getMsg("VAS_FromLocator") + '</label>'));
                                    FromLoctr = VIS.Utility.Util.getValueOfInt($FromLocatorControl.getValue());
                                    $FromLocatorControl.fireValueChanged = function () {
                                        FromLoctr = VIS.Utility.Util.getValueOfInt($FromLocatorControl.getValue());
                                    }


                                    ////towarehouse
                                    var SqlWhere = " M_Locator.M_Warehouse_ID=" + ToWarehouse;
                                    _ToLocatorLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "M_Locator_ID", 0, false, SqlWhere);
                                    $ToLocatorControl = new VIS.Controls.VComboBox("M_Locator_ID", false, false, true, _ToLocatorLookUp, 50);
                                    var Tolocatorctrlwrap = $('<div class="vis-control-wrap">');
                                    var Tolocatorbtnwrap = $('<div class="input-group-append">');
                                    _TolocatCtrl.append(Tolocatorctrlwrap);
                                    _TolocatCtrl.append(Tolocatorbtnwrap);
                                    Tolocatorctrlwrap.append($ToLocatorControl.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append($('<label>' + VIS.Msg.getMsg("VAS_ToLocator") + '</label>'));
                                    ToLoctor = VIS.Utility.Util.getValueOfInt($ToLocatorControl.getValue());

                                    $ToLocatorControl.fireValueChanged = function () {
                                        ToLoctor = VIS.Utility.Util.getValueOfInt($ToLocatorControl.getValue());
                                    }

                                }

                                else {
                                    $root.find(".VAS-Inventorymove").css("display", "none");
                                }
                            }
                        });
                    }
                    $self.setBusy(false);
                });
        }
        //VIS0336-for verifying is user alredy selected on user filter
        function isProductTagExist(pTagid) {
            var ptagItem = $root.find('.VAS_UserTag').find("[item-id='" + pTagid + "']");
            if (ptagItem) {
                if (ptagItem.length > 0) {
                    return false;
                }
            }
            return true;

        };

        function InitEvents() {

            InputCartSearch.on('keyup', function (e) {
                CartName = InputCartSearch.val();
                if (e.keyCode === 13) {
                    LoadCartData();

                }
            });

            InputRefSearch.on('keyup', function (e) {
                RefNo = InputRefSearch.val();
                if (e.keyCode === 13) {
                    LoadCartData();

                }
            });

            ClearFilter.on("click touchstart", function (ev) {
                UserIds = "";
                RefNo = "";
                InputRefSearch.val("");
                CartName = "";
                InputCartSearch.val("");
                $FromDate.oldValue = null;
                $FromDate.setValue("");
                $FromDate.setValue(null);

                $ToDate.oldValue = null;
                $ToDate.setValue("");
                $ToDate.setValue(null);

                $root.find('.VAS_UserTag').find('span').remove();
                LoadCartData();
            });
            $root.find('.VAS-CartLineDetails').find('#VAS-BackArrow_' + $self.windowNo).click(function () {
                LoadCartData();
            });


            $root.find("#selectAllCheckbox").on("change", function () {
                var isChecked = $(this).is(':checked');
                $root.find("#VAS-CartLinesDetails_" + $self.windowNo + " input[type='checkbox']").prop('checked', isChecked);
                Fetchdata();
            });
            $root.find('#VAS-Updatedcheckbox_' + $self.windowNo).on("change", function () {
                if ($(this).is(':checked')) {
                    IsUpdateTrue = true;
                }
                else {
                    IsUpdateTrue = false;
                }

            });
            $root.find('.VAS-CartLineDetails').find('#VAS-CreateLines_' + $self.windowNo).click(function () {

                if (selectedLines.length == 0) {
                    VIS.ADialog.info("VAS_PleaseSelectLines", null, "", "");
                    return;
                }


                if (WindowName == "VAS_InventoryMove") {
                    if (FromLoctr == 0 || ToLoctor == 0) {
                        VIS.ADialog.info("VAS_PleaseSelectLocator", null, "", "");
                        return;
                    }


                    if (FromLoctr == ToLoctor) {
                        VIS.ADialog.info("VAS_InValidCase", null, "", "");
                        return;
                    }
                }
                SaveTransaction();
            });
            $FromDate.getControl().on("change", function () {
                if ($FromDate.getValue() > $ToDate.getValue()) {
                    VIS.ADialog.info("VAS_InCorrectDate", null, "", "");
                    $FromDate.oldValue = null;
                    $FromDate.setValue("");
                    $FromDate.setValue(null);
                    return;
                }
                LoadCartData();
            });
            $ToDate.getControl().on("change", function () {
                if ($FromDate.getValue() > $ToDate.getValue()) {
                    VIS.ADialog.info("VAS_InCorrectDate", null, "", "");
                    $ToDate.oldValue = null;
                    $ToDate.setValue("");
                    $ToDate.setValue(null);

                    return;
                }
                LoadCartData();
            });
        }
        //VIS0336-for storign selected lines in array on the selection of checkbox
        function Fetchdata() {
            selectedLines = [];
            $root.find("#VAS-CartLinesDetails_" + $self.windowNo + " input:checked").each(function () {
                var row = $(this).closest('tr');
                var Code = row.attr('cart-code');
                var ProductId = row.attr('product-id');
                var AttrId = row.attr('attr-id');
                var UOMId = row.attr('uom-id');
                var InventoryCountId = row.attr('inventorycount-id');
                var Qty = row.attr('qty');

                selectedLines.push({
                    Code: Code,
                    ProductId: ProductId,
                    AttrId: AttrId,
                    UOMId: UOMId,
                    InventoryCountId: InventoryCountId,
                    Qty: Qty

                });
            });

        }
        //VIS0336-for saving the records
        function SaveTransaction() {
            $self.setBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/InventoryLines/SaveTransactions",
                type: "POST",
                dataType: "json",
                async: true,
                data: {
                    TransactionID: $self.Record_ID,
                    lstScanDetail: JSON.stringify(selectedLines),
                    IsUpdateTrue: IsUpdateTrue,
                    windowID: window_ID,
                    RefNo: Reference,
                    FromLocatorId: FromLoctr,
                    ToLocatorId: ToLoctor
                },
                success: function (result) {
                    if (JSON.parse(result) == "") {
                        $self.frame.close();
                        VIS.ADialog.info("VAS_RecordSaved", null, "", "");
                    }
                    else {
                        VIS.ADialog.info(result);
                    }
                    $self.setBusy(false);
                },
                error: function () {
                    $self.setBusy(false);

                }
            });

        }
        //VIS0336-forfetching the user for autocomplete cntrl
        function GetUsers(response, value) {
            $.ajax({
                url: VIS.Application.contextUrl + "InventoryLines/GetUsers",
                type: "GET",
                datatype: "json",

                data: {
                    SearchKey: value
                },
                success: function (result) {
                    var result = JSON.parse(result);

                    response($.map(result, function (item) {
                        return {
                            label: item.Name,
                            value: item.Name,
                            ids: item.Key
                        }
                    }));
                    $($self.div).autocomplete("search", "");
                    $($self.div).trigger("focus");
                }
            })
        };
        this.getRoot = function () {
            return $root;
        };

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap" style="visibility: hidden;"></div>');
            $bsyDiv.append($('<div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div>'));
            $self.setBusy(false);
            $root.append($bsyDiv);
        };

        this.setBusy = function (isBusy) {
            if (isBusy) {
                $bsyDiv[0].style.visibility = "visible";
            }
            else {
                $bsyDiv[0].style.visibility = "hidden";
            }
        }
    };

    VAS.VAS_CreateInventoryLines.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.Record_ID = this.frame.getRecord_ID();
        this.Table_ID = this.frame.getAD_Table_ID();
        this.frame.getContentGrid().append(this.getRoot());
        this.initalize();
        this.intialLoad();

    };
    VAS.VAS_CreateInventoryLines.prototype.setHeight = function () {
        return "575";
    };
    VAS.VAS_CreateInventoryLines.prototype.setWidth = function () {
        return "850";
    };

    VAS.VAS_CreateInventoryLines.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
    };
})(VAS, jQuery);
