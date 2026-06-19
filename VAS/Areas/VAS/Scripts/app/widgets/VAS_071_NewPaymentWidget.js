/************************************************************
 * Module Name    : VAS
 * Purpose        : New Payment quick-action widget.
 *                  Opens the AP Payment screen for a new record.
 * chronological  : Development
 * Created Date   : 15 June 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_071_NewPayment        => "New Payment"
 *   VAS_071_NewPaymentSub     => "Pay vendor or settle bill"
 *   VAS_071_CreateNewPayment  => "Create new payment"
 *   VAS_071_OpenPaymentError  => "Unable to open payment screen."
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_071_NewPaymentWidget = function () {
        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-npmt-root">');
        var $card;
        var isOpening = false;

        this.initalize = function () {
            buildShell();
        };

        this.intialLoad = function () {
            setOpening(false);
        };

        function buildShell() {
            var title = msg('VAS_071_NewPayment', 'New Payment');
            var sub = msg('VAS_071_NewPaymentSub', 'Pay vendor or settle bill');
            var aria = msg('VAS_071_CreateNewPayment', 'Create new payment');

            $card = $(
                '<button type="button" class="vas-npmt-card" data-action="new-payment" aria-label="' + esc(aria) + '">' +
                    '<span class="vas-npmt-icon-wrap">' +
                        '<span class="vas-npmt-icon">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round">' +
                                '<line x1="12" y1="5" x2="12" y2="19"></line>' +
                                '<line x1="5" y1="12" x2="19" y2="12"></line>' +
                            '</svg>' +
                        '</span>' +
                    '</span>' +
                    '<span class="vas-npmt-text">' +
                        '<span class="vas-npmt-title">' + esc(title) + '</span>' +
                        '<span class="vas-npmt-sub">' + esc(sub) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', openPaymentWindow);
            $root.append($card);
        }

        function openPaymentWindow() {
            if (isOpening) { return; }

            setOpening(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_071_NewPaymentWidget/GetPaymentWindow',
                dataType: 'json',
                async: true,
                success: function (res) {
                    setOpening(false);
                    var data = parseResponse(res);
                    var windowId = VIS.Utility.Util.getValueOfInt(data.WindowId);
                    if (windowId <= 0) {
                        showOpenError();
                        return;
                    }

                    var query = new VIS.Query();
                    query.addRestriction('C_Payment_ID', VIS.Query.prototype.EQUAL, 0);
                    VIS.viewManager.startWindow(windowId, query);
                },
                error: function () {
                    setOpening(false);
                    showOpenError();
                }
            });
        }

        function parseResponse(res) {
            if (typeof res !== 'string') { return res || {}; }
            try {
                return JSON.parse(res) || {};
            } catch (e) {
                return {};
            }
        }

        function showOpenError() {
            VIS.ADialog.error('', '', msg('VAS_071_OpenPaymentError', 'Unable to open payment screen.'));
        }

        function setOpening(opening) {
            isOpening = opening;
            if ($card) {
                $card.toggleClass('vas-npmt-opening', opening);
                $card.prop('disabled', opening);
            }
        }

        function msg(key, fallback) {
            var value = VIS.Msg.getMsg(key);
            return value && value !== key && value !== '[' + key + ']' ? value : fallback;
        }

        function esc(value) {
            return $('<div>').text(value == null ? '' : value).html();
        }

        this.refreshWidget = function () {
            setOpening(false);
        };

        this.getRoot = function () { return $root; };

        this._teardown = function () {
            if ($card) { $card.off('click'); }
            $root.remove();
        };
    };

    VAS.VAS_071_NewPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_071_NewPaymentWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_071_NewPaymentWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };
    VAS.VAS_071_NewPaymentWidget.prototype.widgetSizeChange = function (widget) { this.widgetInfo = widget; };
    VAS.VAS_071_NewPaymentWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
