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


        //
        var VAS_txtPUQtyForPU = null;
        var VAS_txtSUQtyForPU = null;
        var VAS_txtSUQtyForSU = null;
        var VAS_txtPUQtyForSU = null;
        var VAS_txtSUQtyForCU = null;
        var VAS_txtPUQtyForCU = null;

        //

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
                '<h1>' + VIS.Msg.getMsg("VAS_ProductFormHeader") + '</h1>' +
                '<div class="VAS-input-fields">' +

                '<div class="VAS-input-row">' +
                '<div class="input-group vis-input-wrap VAS-UOM" id="VAS_ddlUOM' + $self.windowNo + '">' + '</div>' +
                '<div class="VAS_convertedUnit">' + VIS.Msg.getMsg("VAS_ProductConvertUnit") + '</div>' +
                '<div id="VAS_ControlDiv">' +
                '<div class="input-group vis-input-wrap VAS_AdhocField">' +
                '<div class="vis-control-wrap" id="VAS_ddlPU' + $self.windowNo + '">' + '</div>' +
                '<div class="vis-control-wrap">' +
                '<label for="VAS_txtSUQtyForPU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtSUQtyForPU") + '</label>' +
                '<input type="text" name="VAS_schedule" class="vas-txtbox" value="" placeholder="" data-placeholder=""  id ="VAS_txtSUQtyForPU' + $self.windowNo + '">' +

                '</div>' +
                '<div class="vis-control-wrap vas-txtbox">' +
                '<label for="VAS_txtPUQtyForPU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtPUQtyForPU") + '</label>' +
                '<input type="text" name="VAS_schedule" class="vas-txtbox" value="" placeholder="" data-placeholder="" id="VAS_txtPUQtyForPU' + $self.windowNo + '">' +

                '</div>' +
                '</div>' +


                '<div class="VAS-input-row">' +

                '<div class="input-group vis-input-wrap VAS_AdhocField">' +
                '<div class="vis-control-wrap" id="VAS_ddlSU' + $self.windowNo + '">' +
                '</div>' +
                '<div class="vis-control-wrap">' +
                '<label for="VAS_txtSUQtyForSU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtSUQtyForSU") + '</label>' +
                '<input type="text" class="vas-txtbox" name="VAS_schedule" value="" placeholder="" data-placeholder=""  id ="VAS_txtSUQtyForSU' + $self.windowNo + '">' +

                '</div>' +
                '<div class="vis-control-wrap">' +
                '<label for="VAS_txtPUQtyForSU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtPUQtyForSU") + '</label>' +
                '<input type = "text" class= "vas-txtbox" name = "VAS_schedule1" value = "" placeholder = "" data - placeholder="" id = "VAS_txtPUQtyForSU' + $self.windowNo + '" > ' +

                '</div>' +
                '</div>' +
                '</div>' +


                '<div class="VAS-input-row">' +

                '<div class="input-group vis-input-wrap VAS_AdhocField">' +
                '<div class="vis-control-wrap" id="VAS_ddlCU' + $self.windowNo + '">' +
                '</div>' +
                '<div class="vis-control-wrap vas-txtbox">' +
                '<label for="VAS_txtSUQtyForCU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtSUQtyForCU") + '</label>' +
                '<input type="text" name="VAS_schedule" class="vas-txtbox" value="" placeholder="" data-placeholder=""  id ="VAS_txtSUQtyForCU' + $self.windowNo + '">' +

                '</div>' +
                '<div class="vis-control-wrap vas-txtbox">' +
                '<label for="VAS_txtPUQtyForCU' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_txtPUQtyForCU") + '</label>' +
                '<input type = "text" class= "vas-txtbox" name = "VAS_schedule" value = "" placeholder = "" data - placeholder="" id = "VAS_txtPUQtyForCU' + $self.windowNo + '" > ' +

                '</div>' +
                '</div>' +

                '</div>' +
                '</div>' +


                '<div class="VAS_SaveCancelButtons text-right">' +
                '<button id="VAS_Save' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_Save") + '</button>' +
                '<button id="VAS_Cancel' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_Cancel") + '</button>' +
                '</div>' +
                '<div class="VAS_Footer">' +
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
            var $UOMddlControlWrap = $('<div class="vis-control-wrap">');
            $inputUOM.append($UOMddlControlWrap);
            $UOMddlControlWrap.append($self.cmbUOM.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VAS_ddlUOM") + '</label><span class= "vis-ev-ctrlinfowrap"</span>');
            $inputUOM.append($UOMddlControlWrap);


            var PUlookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "C_UOM_ID", 0, false);
            $self.cmbPU = new VIS.Controls.VComboBox("C_UOM_ID", true, false, true, PUlookup, 150, VIS.DisplayType.TableDir);
            var $PurControlWrap = $('<div class="vis-control-wrap">');
            $PurControlWrap.append('<label>' + VIS.Msg.getMsg("VAS_ddlPUUnit") + '</label><span class= "vis-ev-ctrlinfowrap"</span>');
            $inputPurUnit.append($PurControlWrap);
            $PurControlWrap.append($self.cmbPU.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '));
            $inputPurUnit.append($PurControlWrap);

            var SUlookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "C_UOM_ID", 0, false);
            $self.cmbSU = new VIS.Controls.VComboBox("C_UOM_ID", true, false, true, SUlookup, 150, VIS.DisplayType.TableDir);
            var $SUControlWrap = $('<div class="vis-control-wrap">');
            $SUControlWrap.append('<label>' + VIS.Msg.getMsg("VAS_ddlSUUnit") + '</label><span class= "vis-ev-ctrlinfowrap"</span>')
            $inputSuUnit.append($SUControlWrap);
            $SUControlWrap.append($self.cmbSU.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '));
            $inputSuUnit.append($SUControlWrap);

            var CUlookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "C_UOM_ID", 0, false);
            $self.cmbCU = new VIS.Controls.VComboBox("C_UOM_ID", true, false, true, CUlookup, 150, VIS.DisplayType.TableDir);
            var $CUControlWrap = $('<div class="vis-control-wrap">');
            $CUControlWrap.append('<label>' + VIS.Msg.getMsg("VAS_ddlCUUnit") + '</label><span class= "vis-ev-ctrlinfowrap"</span>')
            $inputCuUnit.append($CUControlWrap);
            $CUControlWrap.append($self.cmbCU.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '));
            $inputCuUnit.append($CUControlWrap);


            /*  hide the phase and projecttask fields if we open from a reuqest window*/

            $root.append(inputDiv);
            busyDiv(false)

            // This function used to restrict the alphabet in textbox
            VAS_Alltxt.on('keypress', function (evt) {

                if (evt.keyCode > 31 && (evt.keyCode < 48 || evt.keyCode > 57))
                    return false;
            })
            //Function used to  calculate the divide rate

            VAS_txtSUQtyForPU.on('focusout', function () {
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
            VAS_txtPUQtyForPU.on('focusout', function () {
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

            VAS_txtSUQtyForSU.on('focusout', function () {
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

            VAS_txtPUQtyForSU.on('focusout', function () {
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
            VAS_txtSUQtyForCU.on('focusout', function () {
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
            VAS_txtPUQtyForCU.on('focusout', function () {
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
            //else if (VAS_txtPUQtyForPU.val() == "") {
            //    VIS.ADialog.info("VAS_txtPUQtyForPUMandatory");
            //    busyDiv(false);
            //    return;
            //}
            //else if (VAS_txtSUQtyForSU.val() == "") {
            //    VIS.ADialog.info("VAS_txtSUQtyForSUMandatory");
            //    busyDiv(false);
            //    return;
            //}
            //else if (VAS_txtPUQtyForSU.val() == "") {
            //    VIS.ADialog.info("VAS_txtPUQtyForSUMandatory");
            //    busyDiv(false);
            //    return;
            //}
            //else if (VAS_txtSUQtyForCU.val() == "") {
            //    VIS.ADialog.info("VAS_txtSUQtyForCUMandatory");
            //    busyDiv(false);
            //    return;
            //}
            //else if (VAS_txtPUQtyForCU.val() == "") {
            //    VIS.ADialog.info("VAS_VAS_txtPUQtyForCUMandatory");
            //    busyDiv(false);
            //    return;
            //}
            var multiplyRateList = [
                {

                    C_UOM_To_ID: $self.cmbPU.getValue(),
                    MultiplyRate: VAS_txtSUQtyForPU.val(),
                    DivideRate: VAS_txtPUQtyForPU.val()
                },
                {

                    C_UOM_To_ID: $self.cmbSU.getValue(),
                    MultiplyRate: VAS_txtSUQtyForSU.val(),
                    DivideRate: VAS_txtPUQtyForSU.val()
                },
                {

                    C_UOM_To_ID: $self.cmbCU.getValue(),
                    MultiplyRate: VAS_txtSUQtyForCU.val(),
                    DivideRate: VAS_txtPUQtyForCU.val(),


                }
            ];
            var uomConversionData = multiplyRateList;

            busyDiv(true);
            $.ajax({
                url: VIS.Application.contextUrl + "Product/SaveUOMConversion",
                data: {
                    C_UOM_ID: $self.cmbUOM.getValue(),
                    multiplyRateList: JSON.stringify(uomConversionData),
                    Product_ID: M_Product_ID,
                    VAS_PurchaseUOM_ID: $self.cmbPU.getValue(),
                    VAS_SalesUOM_ID: $self.cmbSU.getValue(),
                    VAS_ConsumableUOM_ID: $self.cmbCU.getValue()

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
            //recordId = frame.getRecord_ID();
            //tableID = frame.getAD_Table_ID();
            /*  frame.hideHeader(true);*/
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


            return 565;
        };
        // Load form into VIS
        VAS.VAS_ProductUomSetup = VAS.VAS_ProductUomSetup
    };
})(VAS, jQuery);