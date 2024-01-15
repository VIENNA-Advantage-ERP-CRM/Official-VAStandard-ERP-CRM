/*******************************************************
       * Module Name    : VAS
       * Purpose        : Tab Panel For AP Matched PO and MatchedReceipt
       * chronological  : Development
       * Created Date   : 12 January 2024
       * Created by     : VAI066
      ******************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.PoReceiptTabPanel = function () {

        this.record_ID = 0;
        var $self = this;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var lblOrderId = null;
        var lblGrnLineId = null;
        var lblQty = null;
        var lblInvoiceLineId = null;
        var lblProductName = null;
        var lblAttributeSetInstance = null;
        var lblTitle = null;
        var ctx = this.ctx;

        var elements = [
            "VAS_InvoiceLineTitleTabPanel",
            "VAS_InvoiceLine",
            "VAS_OrderLineNo",
            "VAS_ReceiptLine",
            "VAS_Product",
            "VAS_Attribute",
            "VAS_Qty"
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        var wrapperDiv = $("<div class ='vas-apInvPoWrapper'></div>")
        lblTitle = $("<h6 class='vas-apInvPoTtl'>" + VAS.translatedTexts.VAS_InvoiceLineTitleTabPanel + "</h6>");
        
        lblInvoiceLineId = $("<span class = 'vas-apInvPoTitle'>" + VAS.translatedTexts.VAS_InvoiceLine + "</span>");
        var $lblInvoiceLineValue = $('<span class = "vas-apInvPoValue"><label>&nbsp;</label></span>');

        lblOrderId = $("<span class = 'vas-apInvPoTitle'>" + VAS.translatedTexts.VAS_OrderLineNo + "</span>");
        var $lblOrderValue = $('<span class = "vas-apInvPoValue"><label>&nbsp;</label></span>');

        lblGrnLineId = $("<span class = 'vas-apInvPoTitle'>" + VAS.translatedTexts.VAS_ReceiptLine + "</span>");
        var $lblGrnLineValue = $('<span class = "vas-apInvPoValue"><label>&nbsp;</label></span>');

        lblProductName = $("<span class = 'vas-apInvPoTitle'>" + VAS.translatedTexts.VAS_Product + "</span>");
        var $lblProductValue = $('<span class = "vas-apInvPoValue"><label>&nbsp;</label></span>');

        lblAttributeSetInstance = $("<span class = 'vas-apInvPoTitle'>" + VAS.translatedTexts.VAS_Attribute + "</span>");
        var $lblAttributeSetInstanceValue = $('<span class = "vas-apInvPoValue"><label>&nbsp;</label></span>');

        lblQty = $("<span class = 'vas-apInvPoTitle'>" + VAS.translatedTexts.VAS_Qty + "</span>");
        var $lblQtyValue = $('<span class = "vas-apInvPoValue"><label>&nbsp;</label></span>');

        var $root = $('<div class = "root"></div>');

        /*  Intialize UI Elements */
        this.init = function () {
            /*Assign the div space for each label */
            InvoicePoListGroup = $('<div class = "vas-apInvPoListGroup"></div>');
            InvoiceLinediv = $('<div class = "vas-apInvPoSingleItem"></div>');
            OrderLinediv = $('<div class = "vas-apInvPoSingleItem"></div>');
            ReceiptLinediv = $('<div class = "vas-apInvPoSingleItem"></div>');
            Productdiv = $('<div class = "vas-apInvPoSingleItem"></div>');
            Attributediv = $('<div class = "vas-apInvPoSingleItem"></div>');
            Qtydiv = $('<div class = "vas-apInvPoSingleItem"></div>');
            InvoiceLineandPhasediv = $('<div class = "vas-apInvPoListItem mb-2"></div>');

            InvoiceLinediv.append(lblInvoiceLineId).append($lblInvoiceLineValue);
            OrderLinediv.append(lblOrderId).append($lblOrderValue)
            ReceiptLinediv.append(lblGrnLineId).append($lblGrnLineValue)
            Productdiv.append(lblProductName).append($lblProductValue)
            Attributediv.append(lblAttributeSetInstance).append($lblAttributeSetInstanceValue)
            Qtydiv.append(lblQty).append($lblQtyValue);
            InvoiceLineandPhasediv.append(InvoiceLinediv).append(OrderLinediv).append(ReceiptLinediv).append(Productdiv).append(Attributediv).append(Qtydiv);
            InvoicePoListGroup.append(InvoiceLineandPhasediv);
            wrapperDiv.append(lblTitle).append(InvoicePoListGroup);
            /* Attach design to Root division */
            $root.append(wrapperDiv);
            /* Get the tabpanel data returned from ajax method to the lebels */
            this.getPoReceiptData = function (parentID) {
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS/PoReceipt/GetData",
                    type: "GET",
                    dataType: "json",
                    contentType: "application/json; charset=utf-8",
                    data: { parentID: parentID },
                    success: function (data) {
                        if (JSON.parse(data) != "") {
                            data = JSON.parse(data);
                            console.log(data);
                            if (data[0].OrderDocumentNo != "" || data[0].GRNDocumentNo != ""){
                                $lblOrderValue.text(data[0].OrderDocumentNo);
                                $lblGrnLineValue.text(data[0].GRNDocumentNo);
                                $lblInvoiceLineValue.text(data[0].InvoiceDocumentNo);
                                $lblProductValue.text(data[0].ProductName);
                                $lblAttributeSetInstanceValue.text(data[0].AttributeSetInstance);
                                $lblQtyValue.text(data[0].Qty);
                            }
                        }
                    },
                    error: function (eror) {
                        console.log(eror);
                    }
                })
            }

            this.getRoot = function () {
                return $root;
            };
        };

        VAS.PoReceiptTabPanel.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            this.init();
        };
        /* This function to update tab panel based on selected record */
        VAS.PoReceiptTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getPoReceiptData(recordID);
        };
        /* This will set width as per window width in dafault case it is 75% */
        VAS.PoReceiptTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Release all variables from memory */
        VAS.PoReceiptTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblOrderId = null;
            lblGrnLineId = null;
            lblQty = null;
            lblInvoiceLineId = null;
            lblProductName = null;
            lblAttributeSetInstance = null;
            lblTitle = null;
            $lblInvoiceLineValue = null;
            $lblOrderValue = null;
            $lblGrnLineValue = null;
            $lblProductValue = null;
            $lblAttributeSetInstanceValue = null;
            $lblQtyValue = null;
        }
    }
})(VAS, jQuery);