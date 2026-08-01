/**
 * Under Confirmation KPI Widget (Material Transfer Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing total count of dispatched transfers awaiting receipt confirmation.
 *           Renders warning text color (#D78B10) when count >= 1.
 * Prefix  - VAS_169_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Under Confirmation                               | VAS_169_UnderConfirmation
 *  2  | Awaiting confirmation                            | VAS_169_AwaitingConfirmation
 *  3  | Unable to load under confirmation transfers       | VAS_169_UnableToLoadData
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_169_UnderConfirmationWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-under-confirmation-container">');
        var $root = $('<div class="vas-under-confirmation-root">');
        var $valueEl;
        var $metaEl;
        var $busy;
        var widgetObserver = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-under-confirmation-hidden', !show);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function setupWidgetSizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) {
                        $root[0].style.setProperty('--widget-inline-size', width + 'px');
                    }
                }
            });
            widgetObserver.observe($wrapper[0]);
        }

        /* DocStatus list matching backend predicate exactly ('DP', 'UC') */
        var UNDER_CONFIRMATION_STATUS_LIST = "'DP', 'UC'";

        function openTransferList() {
            var where =
                "MMovement.IsActive = 'Y'" +
                " AND MMovement.DocStatus IN (" + UNDER_CONFIRMATION_STATUS_LIST + ")";

            var windowParam = {
                "TabWhereClause": where,
                "TabLayout": "N",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_169_UnderConfirmationWidget/GetUnderConfirmationData',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) { setError(); return; }

                    renderMetric(data || {});
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        function renderMetric(data) {
            var count = Number(data.count || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(count));
                $valueEl.attr('title', formatCount(count));
                $valueEl.attr('aria-live', 'polite');

                // Warning tone (#D78B10) when count >= 1, neutral (#102C3F) when 0
                if (count >= 1) {
                    $valueEl.addClass('vas-under-confirmation-warning');
                } else {
                    $valueEl.removeClass('vas-under-confirmation-warning');
                }
            }

            if ($metaEl) {
                var metaMsg = lbl("VAS_169_AwaitingConfirmation", "Awaiting confirmation");
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
                $valueEl.removeClass('vas-under-confirmation-warning');
            }
            if ($metaEl) {
                var errText = lbl("VAS_169_UnableToLoadData", "Unable to load under confirmation transfers");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        function createWidget() {
            var title = lbl("VAS_169_UnderConfirmation", "Under Confirmation");
            var metaText = lbl("VAS_169_AwaitingConfirmation", "Awaiting confirmation");

            var $card = $(
                '<button type="button" class="vas-under-confirmation-card" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-under-confirmation-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-under-confirmation-value">—</div>' +
                '<div class="vas-under-confirmation-meta">' + escapeHtml(metaText) + '</div>' +
                '</button>'
            );

            $card.on('click', function () { openTransferList(); });

            $valueEl = $card.find('.vas-under-confirmation-value');
            $metaEl = $card.find('.vas-under-confirmation-meta');

            $root.append($card);

            $busy = $('<div class="vas-under-confirmation-busy vas-under-confirmation-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);
            setupWidgetSizeObserver();
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            if (widgetObserver && $wrapper[0]) {
                widgetObserver.unobserve($wrapper[0]);
                widgetObserver = null;
            }
            $root.off();
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_169_UnderConfirmationWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_169_UnderConfirmationWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_169_UnderConfirmationWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_169_UnderConfirmationWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_169_UnderConfirmationWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_169_UnderConfirmationWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
