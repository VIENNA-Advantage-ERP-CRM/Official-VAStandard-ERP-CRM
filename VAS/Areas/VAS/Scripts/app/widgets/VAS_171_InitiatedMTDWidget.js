/**
 * Transfer Initiated MTD KPI Widget (Material Transfer Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing count of transfer documents
 *           initiated (created) in the current calendar month to date.
 *           Neutral tone always (volume is informational, not an alert).
 * Prefix  - VAS_171_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Transfer Initiated MTD                           | VAS_171_TransferInitiatedMTD
 *  2  | This month to date                               | VAS_171_ThisMonthToDate
 *  3  | Unable to load initiated transfers               | VAS_171_UnableToLoadData
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_171_InitiatedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-initiated-mtd-container">');
        var $root = $('<div class="vas-initiated-mtd-root">');
        var $valueEl;
        var $metaEl;
        var $busy;
        var widgetObserver = null;

        function lbl(key, fallback) {
            var msg = VIS.Msg.getMsg(key);
            return (!msg || msg === key || msg === '[' + key + ']') ? fallback : msg;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-initiated-mtd-hidden', !show);
        }

// ===== NEW CODE START — currency format (agent C05, 2026-08-19) =====
        var currencyInfo = { iso: '', symbol: '' };

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function formatCurrency(amount, currInfo) {
            var val = Number(amount || 0);
            var info = currInfo || currencyInfo || {};
            var iso = (info.iso || '').toUpperCase();
            var symbol = info.symbol || '';

            var indianISOs = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
            var isIndian = indianISOs.indexOf(iso) !== -1;

            var formatted = '';
            var absVal = Math.abs(val);

            if (isIndian) {
                if (absVal >= 10000000) {
                    formatted = (val / 10000000).toFixed(2).replace(/\.00$/, '') + ' Cr';
                } else if (absVal >= 100000) {
                    formatted = (val / 100000).toFixed(2).replace(/\.00$/, '') + ' Lk';
                } else {
                    var parts = val.toFixed(2).replace(/\.00$/, '').split('.');
                    var integerPart = parts[0];
                    var decimalPart = parts.length > 1 ? '.' + parts[1] : '';
                    var isNeg = integerPart.charAt(0) === '-';
                    if (isNeg) integerPart = integerPart.substring(1);

                    if (integerPart.length > 3) {
                        var lastThree = integerPart.substring(integerPart.length - 3);
                        var otherNumbers = integerPart.substring(0, integerPart.length - 3);
                        otherNumbers = otherNumbers.replace(/\B(?=(\d{2})+(?!\d))/g, ",");
                        integerPart = otherNumbers + "," + lastThree;
                    }
                    formatted = (isNeg ? '-' : '') + integerPart + decimalPart;
                }
            } else {
                if (absVal >= 1000000000) {
                    formatted = (val / 1000000000).toFixed(2).replace(/\.00$/, '') + 'B';
                } else if (absVal >= 1000000) {
                    formatted = (val / 1000000).toFixed(2).replace(/\.00$/, '') + 'M';
                } else {
                    var parts = val.toFixed(2).replace(/\.00$/, '').split('.');
                    parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ",");
                    formatted = parts.join('.');
                }
            }

            if (symbol) {
                return symbol + ' ' + formatted;
            }
            return formatted;
        }

        function renderMetric(data) {
            var count = Number(data.count || 0);
            if (data.currency) {
                currencyInfo = data.currency;
            }

            if ($valueEl) {
                var formattedVal = formatCount(count);
                $valueEl.text(formattedVal);
                $valueEl.attr('title', String(count));
                $valueEl.attr('aria-live', 'polite');
            }

            if ($metaEl) {
                var metaMsg = lbl('VAS_171_ThisMonthToDate', 'This month to date');
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        function formatCount(value) {
//            return Number(value || 0).toLocaleString(window.navigator.language);
//        }
//
//        function renderMetric(data) {
//            var count = Number(data.count || 0);
//
//            if ($valueEl) {
//                $valueEl.text(formatCount(count));
//                $valueEl.attr('title', formatCount(count));
//                $valueEl.attr('aria-live', 'polite');
//            }
//
//            if ($metaEl) {
//                var metaMsg = lbl('VAS_171_ThisMonthToDate', 'This month to date');
//                $metaEl.text(metaMsg);
//                $metaEl.attr('title', metaMsg);
//            }
//        }
// ----- END OLD CODE -----

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function setupWidgetSizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) {
                        $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        $root[0].style.setProperty('--widget-block-size', entries[i].contentRect.height + 'px');
                    }
                }
            });
            widgetObserver.observe($wrapper[0]);
        }

        /* Build first-of-month and tomorrow date literals for the TabWhereClause.
           Mirrors the backend SQL predicate (Created >= month start AND Created < month end). */
        function monthStartIso() {
            var d = new Date();
            return d.getFullYear() + '-' +
                   String(d.getMonth() + 1).padStart(2, '0') + '-01';
        }

        function tomorrowIso() {
            var d = new Date();
            d.setDate(d.getDate() + 1);
            return d.getFullYear() + '-' +
                   String(d.getMonth() + 1).padStart(2, '0') + '-' +
                   String(d.getDate()).padStart(2, '0');
        }

        /* Drill-through to Material Transfer list filtered to transfers created this month.
           Excludes cancelled (DocStatus NOT IN ('VO','RE')) to match the backend count.
           Uses TabWhereClause so the list row count equals the KPI number. */
        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_171_InitiatedMTDWidget/GetInitiatedMTDData',
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

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) {
                var errText = lbl('VAS_171_UnableToLoadData', 'Unable to load initiated transfers');
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        function createWidget() {
            var title = lbl('VAS_171_TransferInitiatedMTD', 'Transfer Initiated MTD');
            var metaText = lbl('VAS_171_ThisMonthToDate', 'This month to date');

            var $card = $(
                '<div class="vas-initiated-mtd-card" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-initiated-mtd-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-initiated-mtd-value">—</div>' +
                '<div class="vas-initiated-mtd-meta">' + escapeHtml(metaText) + '</div>' +
                '</div>'
            );

            
            $valueEl = $card.find('.vas-initiated-mtd-value');
            $metaEl = $card.find('.vas-initiated-mtd-meta');

            $root.append($card);

            $busy = $('<div class="vas-initiated-mtd-busy vas-initiated-mtd-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VAS.VAS_171_InitiatedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_171_InitiatedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_171_InitiatedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_171_InitiatedMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_171_InitiatedMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_171_InitiatedMTDWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
