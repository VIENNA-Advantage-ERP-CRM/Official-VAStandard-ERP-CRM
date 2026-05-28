/**
 * Bounced Cheques Widget
 * Purpose - KPI card showing count of bounced cheques from AR receipts.
 * Design   - Glass KPI/Summary card per design.md / dashboard-widgets.md: glass
 *            surface, tinted danger icon well, large bold danger-red count,
 *            ACTION pill + explanatory copy.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                | Message Key
 * ----+-----------------------------+--------------------------------
 *  1  | Bounced cheques             | VIS_BouncedCheques
 *  2  | Awaiting follow-up          | VIS_AwaitingFollowUp
 *  3  | ACTION                      | VIS_Action
 *  4  | Customer follow-up required | VIS_CustomerFollowUpRequired
 *  5  | Loading…                    | VIS_Loading
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.BouncedChequesWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-bc-root">');
        var $metricEl;
        /* Busy/loading overlay shown while data is being fetched (initial load + refresh). */
        var $busy;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function loadData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'BouncedCheques/GetBouncedCheques',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;

                    if (data && data.error) {
                        setNoData();
                        return;
                    }

                    renderCount(data);
                },
                error: function () {
                    setNoData();
                },
                complete: function () { showBusy(false); }
            });
        }

        function setNoData() {
            if ($metricEl) {
                $metricEl.text("0");
            }
        }

        function renderCount(data) {
            var count = (data && data.bouncedChequeCount) || 0;
            if ($metricEl) {
                $metricEl.text(Number(count).toLocaleString(window.navigator.language));
            }
        }

        function createWidget() {
            var $card = $('<div class="vas-bc-card">');

            var $header = $(
                '<div class="vas-bc-header">' +
                '<div class="vas-bc-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="1.8" ' +
                'stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="12" cy="12" r="10"/>' +
                '<line x1="12" y1="8" x2="12" y2="13"/>' +
                '<line x1="12" y1="16" x2="12.01" y2="16"/>' +
                '</svg>' +
                '</div>' +
                '<div>' +
                '<div class="vas-bc-title">' + lbl("VIS_BouncedCheques", "Bounced cheques") + '</div>' +
                '<div class="vas-bc-subtitle">' + lbl("VIS_AwaitingFollowUp", "Awaiting follow-up") + '</div>' +
                '</div>' +
                '</div>'
            );

            $metricEl = $('<div class="vas-bc-metric">—</div>');

            var $why = $('<div class="vas-bc-why-wrap">');
            /*var $pill = $('<span class="vas-bc-why-pill">' + lbl("VIS_Action", "ACTION") + '</span>');*/
            var $whyText = $('<span class="vas-bc-why-text">' +
                lbl("VIS_CustomerFollowUpRequired", "Customer follow-up required") +
                '</span>');

            $why.append($whyText);
            $card.append($header).append($metricEl).append($why);
            $root.append($card);

            $busy = $('<div class="vas-bc-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.BouncedChequesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.BouncedChequesWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.BouncedChequesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.BouncedChequesWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);
