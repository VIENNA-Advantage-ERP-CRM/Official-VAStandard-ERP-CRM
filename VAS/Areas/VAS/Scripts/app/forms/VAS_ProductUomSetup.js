/*******************************************************
    * Module Name    : VA Standard
    * Purpose        : Save the UOM Conversion
    * Chronological  : Development
    * VAI050         : 20 June 2024
******************************************************/

; VAS = window.VAS || {};
/*$self-invoking function no need to call ,automatically call on pageload*/
; (function (VAS, $) {
    /*Form Class function fullnamespace*/
    VAS.VAS_ProductUomSetup = function () {
        this.frame;
        this.windowNo;
        var $root = null;
        var $bsyDiv = null;
        var inputDiv = null;
        var $self = this;
        var inputSave = null;
        var VAS_txtPUQtyForPU = null;
        var VAS_txtSUQtyForPU = null;
        var VAS_txtSUQtyForSU = null;
        var VAS_txtPUQtyForSU = null;
        var VAS_txtSUQtyForCU = null;
        var VAS_txtPUQtyForCU = null;
        var VAS_Alltxt = null;
        var C_UOM_ID = null;
        var M_Product_ID = null;
        var rate1 = null;
        var rate2 = null;
        var one = 1.0;
        //

        /*for Initial design*/
        function initializeComponent() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div></div>');
            $root = $('<div>');

            $root.append($bsyDiv);
            busyDiv(true);
            inputDiv = $('<div class="VAS-flyout-body vis-formouterwrpdiv">' +

                '<div class="firstRow">' +
                '<div class="VAS-input-fields">' +


                '<div class="VIS_Pref_show">' +
                '<div class="VIS_Pref_dd">' +
                '<div class="input-group vis-input-wrap VAS-UOM" id="VAS_ddlUOM' + $self.windowNo + '">' + '</div></div></div>' +
                '<div class="VAS_convertedUnit">' + VIS.Msg.getMsg("VAS_ProductConvertUnit") + '</div>' +
                '<div id="VAS_ControlDiv">' +
                '<div class="VIS_Pref_show">' +
                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap" id = "VAS_ddlPU' + $self.windowNo + '" ></div>' +
                '</div> ' +

                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap vas-txtbox">' +
                '<div class="vis-control-wrap">' +
                '<input type="text"  class="vas-txtbox" value="1" placeholder="" data-placeholder=""  id ="VAS_txtSUQtyForPU' + $self.windowNo + '">' +
                '<label for="VAS_txtSUQtyForPU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtSUQtyForPU") + '</label>' +
                '</div > ' +
                '</div> ' +
                '</div> ' +

                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap vas-txtbox">' +
                '<div class="vis-control-wrap">' +
                '<input type="text"  class="vas-txtbox" value="1" placeholder="" data-placeholder="" id="VAS_txtPUQtyForPU' + $self.windowNo + '">' +
                '<label for="VAS_txtPUQtyForPU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtPUQtyForPU") + '</label>' +
                '</div > ' +
                '</div> ' +
                '</div> ' +
                '</div> ' +

                '<div class="VIS_Pref_show">' +
                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap" id = "VAS_ddlSU' + $self.windowNo + '" ></div>' +
                '</div> ' +

                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap vas-txtbox">' +
                '<div class="vis-control-wrap">' +
                '<input type="text" class="vas-txtbox"  value="1" placeholder="" data-placeholder=""  id ="VAS_txtSUQtyForSU' + $self.windowNo + '">' +
                '<label for="VAS_txtSUQtyForSU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtSUQtyForSU") + '</label>' +
                '</div > ' +
                '</div> ' +
                '</div> ' +

                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap vas-txtbox">' +
                '<div class="vis-control-wrap">' +
                '<input type = "text" class= "vas-txtbox"  value = "1" placeholder = "" data-placeholder="" id = "VAS_txtPUQtyForSU' + $self.windowNo + '" > ' +
                '<label for="VAS_txtPUQtyForSU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtPUQtyForSU") + '</label>' +
                '</div > ' +
                '</div> ' +
                '</div> ' +
                '</div> ' +


                '<div class="VIS_Pref_show">' +
                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap" id = "VAS_ddlCU' + $self.windowNo + '" ></div>' +
                '</div> ' +
                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap vas-txtbox">' +
                '<div class="vis-control-wrap">' +
                '<input type="text" class="vas-txtbox" value="1" placeholder="" data-placeholder=""  id ="VAS_txtSUQtyForCU' + $self.windowNo + '">' +
                '<label for="VAS_txtSUQtyForCU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtSUQtyForCU") + '</label>' +
                '</div > ' +
                '</div> ' +
                '</div> ' +
                '<div class="VIS_Pref_dd">' +
                '<div class= "input-group vis-input-wrap vas-txtbox">' +
                '<div class="vis-control-wrap">' +

                '<input type = "text" class= "vas-txtbox"  value = "1" placeholder = "" data-placeholder="" id = "VAS_txtPUQtyForCU' + $self.windowNo + '"> ' +
                '<label for="VAS_txtPUQtyForCU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtPUQtyForCU") + '</label>' +
                '</div > ' +
                '</div> ' +
                '</div> ' +
                '</div> ' +

                '<div class="VAS_SaveCancelButtons text-right">' +
                '<button id="VAS_Cancel' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_Cancel") + '</button>' +
                '<button id="VAS_Save' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_Save") + '</button>' +
                '</div>' +
                '</div>');





            /* find the varibales from html*/
            $inputUOM = inputDiv.find('#VAS_ddlUOM' + $self.windowNo);
            $inputPurUnit = inputDiv.find('#VAS_ddlPU' + $self.windowNo);
            $inputSuUnit = inputDiv.find('#VAS_ddlSU' + $self.windowNo);
            $inputCuUnit = inputDiv.find('#VAS_ddlCU' + $self.windowNo);

            $inputTaskField = inputDiv.find('.VAS_AdhocField');
            VAS_txtSUQtyForPU = inputDiv.find('#VAS_txtSUQtyForPU' + $self.windowNo);
            VAS_txtPUQtyForPU = inputDiv.find('#VAS_txtPUQtyForPU' + $self.windowNo);
            VAS_txtSUQtyForSU = inputDiv.find('#VAS_txtSUQtyForSU' + $self.windowNo);
            VAS_txtPUQtyForSU = inputDiv.find('#VAS_txtPUQtyForSU' + $self.windowNo);
            VAS_txtSUQtyForCU = inputDiv.find('#VAS_txtSUQtyForCU' + $self.windowNo);
            VAS_txtPUQtyForCU = inputDiv.find('#VAS_txtPUQtyForCU' + $self.windowNo);
            VAS_Alltxt = inputDiv.find('.vas-txtbox');

            C_UOM_ID = VIS.Env.getCtx().getWindowContext($self.windowNo, "C_UOM_ID");
            M_Product_ID = VIS.Env.getCtx().getWindowContext($self.windowNo, "M_Product_ID");


            inputSave = inputDiv.find('#VAS_Save' + $self.windowNo);
            inputCancel = inputDiv.find('#VAS_Cancel' + $self.windowNo);



            /**/
            var UOMlookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "C_UOM_ID", 0, false);
            $self.cmbUOM = new VIS.Controls.VComboBox("C_UOM_ID", true, true, true, UOMlookup, 150, VIS.DisplayType.TableDir);
            $self.cmbUOM.setValue(C_UOM_ID);
            var $UOMddlControlWrap = $('<div class="vis-control-wrap ddlPadding">');
            $inputUOM.append($UOMddlControlWrap);
            $UOMddlControlWrap.append($self.cmbUOM.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VAS_ddlUOM") + '</label><span class= "vis-ev-ctrlinfowrap"</span>');
            $inputUOM.append($UOMddlControlWrap);


            var PUlookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "C_UOM_ID", 0, false);
            $self.cmbPU = new VIS.Controls.VComboBox("C_UOM_ID", true, false, true, PUlookup, 150, VIS.DisplayType.TableDir);
            $self.cmbPU.setValue(C_UOM_ID);
            var $PurControlWrap = $('<div class="vis-control-wrap ddlPadding">');
            $inputPurUnit.append($PurControlWrap);
            $PurControlWrap.append($self.cmbPU.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VAS_ddlPUUnit") + '</label><span class= "vis-ev-ctrlinfowrap"</span>');;
            $inputPurUnit.append($PurControlWrap);

            var SUlookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "C_UOM_ID", 0, false);
            $self.cmbSU = new VIS.Controls.VComboBox("C_UOM_ID", true, false, true, SUlookup, 150, VIS.DisplayType.TableDir);
            $self.cmbSU.setValue(C_UOM_ID);
            var $SUControlWrap = $('<div class="vis-control-wrap ddlPadding">');
            $inputSuUnit.prepend($SUControlWrap);
            $SUControlWrap.append($self.cmbSU.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VAS_ddlSUUnit") + '</label><span class= "vis-ev-ctrlinfowrap"</span>');
            $inputSuUnit.append($SUControlWrap);

            var CUlookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "C_UOM_ID", 0, false);
            $self.cmbCU = new VIS.Controls.VComboBox("C_UOM_ID", true, false, true, CUlookup, 150, VIS.DisplayType.TableDir);
            $self.cmbCU.setValue(C_UOM_ID);
            var $CUControlWrap = $('<div class="vis-control-wrap ddlPadding">');
            $inputCuUnit.prepend($CUControlWrap);
            $CUControlWrap.append($self.cmbCU.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VAS_ddlCUUnit") + '</label><span class= "vis-ev-ctrlinfowrap"</span>');
            $inputCuUnit.append($CUControlWrap);


            $root.append(inputDiv);
            busyDiv(false)

            // This function used to restrict the alphabet in textbox
            VAS_Alltxt.on('keypress', function (evt) {

                if (evt.keyCode > 31 && (evt.keyCode < 48 || evt.keyCode > 57))
                    return false;
            })

            //Function used to  calculate the divide rate

            VAS_txtSUQtyForPU.on('change', function () {
                rate1 = VAS_txtSUQtyForPU.val();
                rate2 = VIS.Env.ZERO;
                one = 1.0;
                if (rate1 != 0) {
                    rate2 = (one / rate1).toFixed(12);
                    VAS_txtPUQtyForPU.val(rate2);
                }
                else {
                    VAS_txtPUQtyForPU.val(0);
                }
            })

            //Function used to  calculate the multiply rate
            VAS_txtPUQtyForPU.on('change', function () {
                rate1 = VAS_txtPUQtyForPU.val();
                rate2 = VIS.Env.ZERO;
                one = 1.0;
                if (rate1 != 0) {
                    rate2 = (one / rate1).toFixed(12);
                    VAS_txtSUQtyForPU.val(rate2);
                }
                else {
                    VAS_txtSUQtyForPU.val(0);
                }
            })

            //Function used to  calculate the divide rate

            VAS_txtSUQtyForSU.on('change', function () {
                rate1 = VAS_txtSUQtyForSU.val();
                rate2 = VIS.Env.ZERO;
                one = 1.0;
                if (rate1 != 0) {
                    rate2 = (one / rate1).toFixed(12);
                    VAS_txtPUQtyForSU.val(rate2);
                }
                else {
                    VAS_txtPUQtyForSU.val(0);
                }
            })

            //Function used to  calculate the multiply rate

            VAS_txtPUQtyForSU.on('change', function () {
                rate1 = VAS_txtPUQtyForSU.val();
                rate2 = VIS.Env.ZERO;
                one = 1.0;
                if (rate1 != 0) {
                    rate2 = (one / rate1).toFixed(12);
                    VAS_txtSUQtyForSU.val(rate2);
                }
                else {
                    VAS_txtSUQtyForSU.val(0);
                }
            })

            //Function used to  calculate the divide rate
            VAS_txtSUQtyForCU.on('change', function () {
                rate1 = VAS_txtSUQtyForCU.val();
                rate2 = VIS.Env.ZERO;
                one = 1.0;
                if (rate1 != 0) {
                    rate2 = (one / rate1).toFixed(12);
                    VAS_txtPUQtyForCU.val(rate2);
                }
                else {
                    VAS_txtPUQtyForCU.val(0);
                }
            })

            //Function used to  calculate the multiply rate
            VAS_txtPUQtyForCU.on('change', function () {
                rate1 = VAS_txtPUQtyForCU.val();
                rate2 = VIS.Env.ZERO;
                one = 1.0;
                if (rate1 != 0) {
                    rate2 = (one / rate1).toFixed(12);
                    VAS_txtSUQtyForCU.val(rate2);
                }
                else {
                    VAS_txtSUQtyForCU.val(0);
                }
            })

            //SET qty 1 and disabled textbox when base uom and selected uom same

            $self.cmbPU.fireValueChanged = function () {
                if (C_UOM_ID == $self.cmbPU.getValue()) {
                    VAS_txtSUQtyForPU.prop('disabled', true);
                    VAS_txtPUQtyForPU.prop('disabled', true);
                    VAS_txtSUQtyForPU.val(1);
                    VAS_txtPUQtyForPU.val(1);
                }
                else {
                    VAS_txtSUQtyForPU.prop('disabled', false);
                    VAS_txtPUQtyForPU.prop('disabled', false);
                    VAS_txtSUQtyForPU.val("");
                    VAS_txtPUQtyForPU.val("");
                }
            }

            //SET qty 1 and disabled textbox when base uom and selected uom same

            $self.cmbSU.fireValueChanged = function () {
                if (C_UOM_ID == $self.cmbSU.getValue()) {
                    VAS_txtSUQtyForSU.prop('disabled', true);
                    VAS_txtPUQtyForSU.prop('disabled', true);
                    VAS_txtSUQtyForSU.val(1);
                    VAS_txtPUQtyForSU.val(1);
                }
                else {
                    VAS_txtSUQtyForSU.prop('disabled', false);
                    VAS_txtPUQtyForSU.prop('disabled', false);
                    VAS_txtSUQtyForSU.val("");
                    VAS_txtPUQtyForSU.val("");
                }
            }

            //SET qty 1 and disabled textbox when base uom and selected uom same

            $self.cmbCU.fireValueChanged = function () {
                if (C_UOM_ID == $self.cmbCU.getValue()) {
                    VAS_txtSUQtyForCU.prop('disabled', true);
                    VAS_txtPUQtyForCU.prop('disabled', true);
                    VAS_txtSUQtyForCU.val(1);
                    VAS_txtPUQtyForCU.val(1);
                }
                else {
                    VAS_txtSUQtyForCU.prop('disabled', false);
                    VAS_txtPUQtyForCU.prop('disabled', false);
                    VAS_txtSUQtyForCU.val("");
                    VAS_txtPUQtyForCU.val("");
                }
            }

            /*      click function on save button */
            inputSave.on(VIS.Events.onTouchStartOrClick, function () {
                saveTask();
            });

            /*      click function on save button */
            inputCancel.on(VIS.Events.onTouchStartOrClick, function () {
                $self.frame.close();
            });
        }



        /* save task functionality to send the data to server for save the UOM conversion data*/
        function saveTask() {
            if ($self.cmbPU.getValue() == null) {
                VIS.ADialog.info("VAS_PuUnitMandatory");
                busyDiv(false);
                return;
            }
            else if ($self.cmbSU.getValue() == null) {
                VIS.ADialog.info("VAS_SuUnitMandatory");
                busyDiv(false);
                return;
            }
            else if ($self.cmbCU.getValue() == null) {
                VIS.ADialog.info("VAS_CuUnitMandatory");
                busyDiv(false);
                return;
            }
            else if (VAS_txtSUQtyForPU.val() == "" || VAS_txtSUQtyForPU.val() == 0 || VAS_txtPUQtyForPU.val() == "" || VAS_txtPUQtyForPU.val() == 0
                || VAS_txtSUQtyForSU.val() == "" || VAS_txtSUQtyForSU.val() == 0 || VAS_txtPUQtyForSU.val() == "" || VAS_txtPUQtyForSU.val() == 0
                || VAS_txtSUQtyForCU.val() == "" || VAS_txtSUQtyForCU.val() == 0 || VAS_txtPUQtyForCU.val() == "" || VAS_txtPUQtyForCU.val() == 0) {
                VIS.ADialog.info("ProductUOMConversionRateError");
                busyDiv(false);
                return;
            }

            //  Get the values from the dropdowns
            var puValue = $self.cmbPU.getValue();
            var suValue = $self.cmbSU.getValue();
            var cuValue = $self.cmbCU.getValue();
            var multiplyRateList = [];

            // Get the values for the quantity fields
            var qtyForPU = VAS_txtSUQtyForPU.val();
            var qtyForSU = VAS_txtSUQtyForSU.val();
            var qtyForCU = VAS_txtSUQtyForCU.val();

            if (puValue === suValue && suValue === cuValue) {
                // All values are the same
                if (qtyForPU === qtyForSU && qtyForSU === qtyForCU) {
                    multiplyRateList.push({
                        C_UOM_To_ID: puValue,
                        MultiplyRate: qtyForPU,
                        DivideRate: VAS_txtPUQtyForPU.val()
                    });
                } else {

                    // Quantity values are different for the same dropdown values
                    VIS.ADialog.info("VAS_AllUnitSameButDifferQty");
                    busyDiv(false);
                   
                    return; 
                }
            } else if (puValue === suValue || puValue === cuValue || suValue === cuValue) {
                // Two values are the same
                if (puValue === suValue) {
                    if (qtyForPU !== qtyForSU) {
                        VIS.ADialog.info("VAS_UnitOfPUAndSUButDifferQty");
                        busyDiv(false);
                   
                        return; 
                    }
                    multiplyRateList.push({
                        C_UOM_To_ID: puValue,
                        MultiplyRate: qtyForPU,
                        DivideRate: VAS_txtPUQtyForPU.val()
                    });
                    multiplyRateList.push({
                        C_UOM_To_ID: cuValue,
                        MultiplyRate: VAS_txtSUQtyForCU.val(),
                        DivideRate: VAS_txtPUQtyForCU.val()
                    });
                } else if (puValue === cuValue) {
                    if (qtyForPU !== qtyForCU) {
                        VIS.ADialog.info("VAS_UnitOfPUAndCUButDifferQty");
                        busyDiv(false);
                        return; 
                    }
                    multiplyRateList.push({
                        C_UOM_To_ID: puValue,
                        MultiplyRate: qtyForPU,
                        DivideRate: VAS_txtPUQtyForPU.val()
                    });
                    multiplyRateList.push({
                        C_UOM_To_ID: suValue,
                        MultiplyRate: VAS_txtSUQtyForSU.val(),
                        DivideRate: VAS_txtPUQtyForSU.val()
                    });
                } else if (suValue === cuValue) {
                    if (qtyForSU !== qtyForCU) {
                        VIS.ADialog.info("VAS_UnitOfSUAndCUButDifferQty");
                        busyDiv(false);
                        return;
                    }
                    multiplyRateList.push({
                        C_UOM_To_ID: puValue,
                        MultiplyRate: qtyForPU,
                        DivideRate: VAS_txtPUQtyForPU.val()
                    });
                    multiplyRateList.push({
                        C_UOM_To_ID: suValue,
                        MultiplyRate: qtyForSU,
                        DivideRate: VAS_txtPUQtyForSU.val()
                    });
                }
            } else {
                // All values are different
                multiplyRateList.push({
                    C_UOM_To_ID: puValue,
                    MultiplyRate: qtyForPU,
                    DivideRate: VAS_txtPUQtyForPU.val()
                });
                multiplyRateList.push({
                    C_UOM_To_ID: suValue,
                    MultiplyRate: qtyForSU,
                    DivideRate: VAS_txtPUQtyForSU.val()
                });
                multiplyRateList.push({
                    C_UOM_To_ID: cuValue,
                    MultiplyRate: qtyForCU,
                    DivideRate: VAS_txtPUQtyForCU.val()
                });
            }

            var uomConversionData = multiplyRateList;

            busyDiv(true);
            $.ajax({
                url: VIS.Application.contextUrl + "Product/SaveUOMConversion",
                data: {
                    C_UOM_ID: $self.cmbUOM.getValue(),
                    multiplyRateList: JSON.stringify(uomConversionData),
                    Product_ID: M_Product_ID,
                    VAS_PurchaseUOM_ID: puValue,
                    VAS_SalesUOM_ID: suValue,
                    VAS_ConsumableUOM_ID: cuValue

                },
                contentType: "application/json; charset=utf-8",
                success: function (result) {
                    result = JSON.parse(result);
                    if (result != null) {
                        if (result.Status == "1") {
                            VIS.ADialog.info("", "", result.message);
                            busyDiv(false);
                            $self.frame.close();
                        }
                        else {
                            VIS.ADialog.info("", "", result.message);
                            busyDiv(false);
                        }
                    }
                },
                error: function (eror) {
                    VIS.ADialog.info("", "", VIS.Msg.getMsg("VAS_UOMConvNotSaved"));
                    busyDiv(false);
                    console.log(eror);
                }
            });
        };


        /* dispose to clean the memory*/
        this.disposeComponent = function () {
            if ($root)
                $root.remove();
            $root = null;
            $bsyDiv = null;
            inputDiv = null;
            $self = null;
            inputSave = null;
            VAS_txtPUQtyForPU = null;
            VAS_txtSUQtyForPU = null;
            VAS_txtSUQtyForSU = null;
            VAS_txtPUQtyForSU = null;
            VAS_txtSUQtyForCU = null;
            VAS_txtPUQtyForCU = null;
            VAS_Alltxt = null;
            C_UOM_ID = null;
            M_Product_ID = null;
            rate1 = null;
            rate2 = null;
            one = null;
        };


        this.initalize = function () {
            // load by java script
            initializeComponent();
        }

        this.getRoot = function () {
            return $root;
        };

        /* busydiv indicator*/
        function busyDiv(Value) {
            if (Value) {
                $bsyDiv[0].style.visibility = 'visible';
            }
            else {
                $bsyDiv[0].style.visibility = 'hidden';
            }
        };

        VAS.VAS_ProductUomSetup.prototype.init = function (windowNo, frame) {
            /*Assign to this Variable */
            this.frame = frame;
            this.windowNo = windowNo;
            this.initalize();
            this.frame.getContentGrid().append(this.getRoot());
        };

        /*Must implement dispose*/
        VAS.VAS_ProductUomSetup.prototype.dispose = function () {
            /*CleanUp Code */
            /*dispose this component*/
            this.disposeComponent();
            this.frame = null;
            this.windowNo = null;
            C_UOM_ID = null;
        };

        /* To set the width of frame */
        VAS.VAS_ProductUomSetup.prototype.setWidth = function () {
            return 1000;
        };

        /*  TO set the height of frame*/
        VAS.VAS_ProductUomSetup.prototype.setHeight = function () {


            return 480;
        };
        // Load form into VIS
        VAS.VAS_ProductUomSetup = VAS.VAS_ProductUomSetup
    };
})(VAS, jQuery);