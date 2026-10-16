/**
 * Locator Transfer Matrix Widget (Material Transfer Dashboard)
 * Purpose - Heat-map matrix of stock moved between locators (from-locator rows x to-locator
 *           columns) for the selected month/year. Shows the busiest locators; cell = quantity.
 * Prefix  - VAS_177_
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

    /* Falls back to the English text when the AD_Message key is not defined. */
    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function escapeHtml(v) {
        return String(v == null ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MAX_AXIS = 5;

    function formatQty(v) {
        var n = Number(v || 0);
        return n.toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
    }

    VAS.VAS_177_LocatorTransferMatrixWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-177-container">');
        var $root = $('<div class="vas-177-root">');
        var widgetObserver = null;

        var now = new Date();
        var selMonth = now.getMonth() + 1;
        var selYear = now.getFullYear();
        var loaded = false;
        var cells = [];

        function build() {
            var monthOpts = '';
            for (var m = 0; m < 12; m++) {
                monthOpts += '<option value="' + (m + 1) + '"' + (m + 1 === selMonth ? ' selected' : '') + '>' + MONTHS[m] + '</option>';
            }
            var yearOpts = '';
            for (var y = selYear; y >= selYear - 4; y--) {
                yearOpts += '<option value="' + y + '"' + (y === selYear ? ' selected' : '') + '>' + y + '</option>';
            }

            $root.html(
                '<div class="vas-177-header-row">' +
                    '<div class="vas-177-left-cluster">' +
                        '<div class="vas-177-icon-well"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect></svg></div>' +
                        '<div class="vas-177-title-block">' +
                            '<div class="vas-177-title">' + escapeHtml(lbl('VAS_177_Title', 'Locator Transfer Matrix')) + '</div>' +
                            '<div class="vas-177-subtitle">' + escapeHtml(lbl('VAS_177_Subtitle', 'Quantity moved from locator to locator')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-177-filters">' +
                        '<select class="vas-177-sel vas-177-msel">' + monthOpts + '</select>' +
                        '<select class="vas-177-sel vas-177-ysel">' + yearOpts + '</select>' +
                    '</div>' +
                '</div>' +
                '<div class="vas-177-table-wrap"><div class="vas-177-body"></div></div>'
            );
            $wrapper.empty().append($root);

            $root.on('change', '.vas-177-sel', function () {
                selMonth = parseInt($root.find('.vas-177-msel').val(), 10);
                selYear = parseInt($root.find('.vas-177-ysel').val(), 10);
                load();
            });
            setupWidgetSizeObserver();
        }

        function setupWidgetSizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) { $root[0].style.setProperty('--widget-inline-size', width + 'px'); }
                }
            });
            widgetObserver.observe($wrapper[0]);
        }

        function topKeys(field) {
            var tot = {};
            cells.forEach(function (c) { tot[c[field]] = (tot[c[field]] || 0) + Number(c.Qty || 0); });
            return Object.keys(tot).sort(function (a, b) { return tot[b] - tot[a]; }).slice(0, MAX_AXIS);
        }

        function render() {
            var $body = $root.find('.vas-177-body');
            if (!loaded) {
                $body.html('<div class="vas-177-empty">' + escapeHtml(lbl('VAS_177_Loading', 'Loading…')) + '</div>');
                return;
            }
            if (!cells.length) {
                $body.html('<div class="vas-177-empty">' + escapeHtml(lbl('VAS_177_Empty', 'No locator transfers for the selected period')) + '</div>');
                return;
            }

            var rows = topKeys('FromLocator');
            var cols = topKeys('ToLocator');
            var map = {};
            var max = 0;
            cells.forEach(function (c) {
                map[c.FromLocator + '\u0001' + c.ToLocator] = c;
                if (rows.indexOf(c.FromLocator) !== -1 && cols.indexOf(c.ToLocator) !== -1) {
                    max = Math.max(max, Number(c.Qty || 0));
                }
            });

            var html = '<table class="vas-177-table"><thead><tr><th class="vas-177-corner">' +
                escapeHtml(lbl('VAS_177_FromTo', 'From \\ To')) + '</th>';
            cols.forEach(function (c) { html += '<th title="' + escapeHtml(c) + '">' + escapeHtml(c) + '</th>'; });
            html += '</tr></thead><tbody>';
            rows.forEach(function (r) {
                html += '<tr><th class="vas-177-rowhead" title="' + escapeHtml(r) + '">' + escapeHtml(r) + '</th>';
                cols.forEach(function (c) {
                    var cell = map[r + '\u0001' + c];
                    if (!cell) { html += '<td class="vas-177-cell vas-177-cell--none">–</td>'; return; }
                    var q = Number(cell.Qty || 0);
                    var alpha = max > 0 ? (0.12 + 0.6 * (q / max)) : 0.12;
                    html += '<td class="vas-177-cell" style="background-color: rgba(0,131,218,' + alpha.toFixed(2) + ')" title="' +
                        escapeHtml(r + ' → ' + c + ': ' + formatQty(q) + ' (' + cell.Docs + ' ' + lbl('VAS_177_Docs', 'documents') + ')') + '">' +
                        escapeHtml(formatQty(q)) + '</td>';
                });
                html += '</tr>';
            });
            html += '</tbody></table>';
            $body.html(html);
        }

        function load() {
            loaded = false;
            render();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_177_LocatorTransferMatrixWidget/GetLocatorMatrix',
                type: 'GET',
                cache: false,
                data: { month: selMonth, year: selYear },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }
                    loaded = true;
                    if (!data || data.error) {
                        cells = [];
                        $root.find('.vas-177-body').html('<div class="vas-177-empty">' + escapeHtml(lbl('VAS_177_LoadError', 'Unable to load data')) + '</div>');
                        return;
                    }
                    cells = data.cells || [];
                    render();
                },
                error: function () {
                    loaded = true;
                    cells = [];
                    $root.find('.vas-177-body').html('<div class="vas-177-empty">' + escapeHtml(lbl('VAS_177_LoadError', 'Unable to load data')) + '</div>');
                }
            });
        }

        this.Initalize = function () {
            build();
            load();
        };

        this.refreshWidget = function () { load(); };

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

    VAS.VAS_177_LocatorTransferMatrixWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_177_LocatorTransferMatrixWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_177_LocatorTransferMatrixWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_177_LocatorTransferMatrixWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_177_LocatorTransferMatrixWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_177_LocatorTransferMatrixWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
