/**
 * Locator Transfer Matrix Widget (Material Transfer Dashboard)
 * Purpose - Heat-map matrix of stock moved between locators (from-locator rows x to-locator
 *           columns) for the selected month/year. The number of rows/columns adapts to the
 *           widget size (no inner scrollbar); the footer says how many locators are shown.
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
    var KEY_SEP = '\u0001';

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
        var bodyObserver = null;

        var now = new Date();
        var selMonth = now.getMonth() + 1;
        var selYear = now.getFullYear();
        var loaded = false;
        var cells = [];
        var maxRows = 3;
        var maxCols = 3;
        var openMenu = '';

        function build() {
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
                        '<div class="vas-177-dropdown" data-dd="m"><button type="button" class="vas-177-dropdown-btn"><span class="vas-177-dd-lbl">' + MONTHS[selMonth - 1] + '</span></button></div>' +
                        '<div class="vas-177-dropdown" data-dd="y"><button type="button" class="vas-177-dropdown-btn"><span class="vas-177-dd-lbl">' + selYear + '</span></button></div>' +
                    '</div>' +
                '</div>' +
                '<div class="vas-177-table-wrap"><div class="vas-177-table-body"></div></div>' +
                '<div class="vas-177-footer"></div>'
            );
            $wrapper.empty().append($root);

            $root.on('click', '.vas-177-dropdown-btn', function (e) {
                e.stopPropagation();
                toggleMenu($(this).closest('.vas-177-dropdown').attr('data-dd'));
            });
            $root.on('click', '.vas-177-dd-option', function (e) {
                e.stopPropagation();
                var key = $(this).closest('.vas-177-dropdown').attr('data-dd');
                var val = parseInt($(this).attr('data-v'), 10);
                if (key === 'm') { selMonth = val; } else { selYear = val; }
                $(this).closest('.vas-177-dropdown').find('.vas-177-dd-lbl').text($(this).text());
                closeMenus();
                load();
            });
            $(document).on('click.vas177', closeMenus);

            setupObservers();
        }

        function closeMenus() {
            openMenu = '';
            $root.find('.vas-177-dropdown-menu').remove();
        }

        function toggleMenu(key) {
            var wasOpen = (openMenu === key);
            closeMenus();
            if (wasOpen) { return; }
            openMenu = key;
            var html = '<div class="vas-177-dropdown-menu">';
            if (key === 'm') {
                html += '<div class="vas-177-dd-option' + (selMonth === 0 ? ' vas-177-dd-option--sel' : '') + '" data-v="0">' + escapeHtml(lbl('VAS_177_AllMonths', 'All Months')) + '</div>';
                MONTHS.forEach(function (m, i) {
                    html += '<div class="vas-177-dd-option' + (selMonth === i + 1 ? ' vas-177-dd-option--sel' : '') + '" data-v="' + (i + 1) + '">' + m + '</div>';
                });
            } else {
                for (var y = now.getFullYear(); y >= now.getFullYear() - 4; y--) {
                    html += '<div class="vas-177-dd-option' + (selYear === y ? ' vas-177-dd-option--sel' : '') + '" data-v="' + y + '">' + y + '</div>';
                }
            }
            html += '</div>';
            $root.find('.vas-177-dropdown[data-dd="' + key + '"]').append(html);
        }

        function setupObservers() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) { $root[0].style.setProperty('--widget-inline-size', width + 'px'); }
                }
            });
            widgetObserver.observe($wrapper[0]);

            /* Adaptive size: how many locator rows / columns fit the body. */
            bodyObserver = new ResizeObserver(function () { recalc(); });
            bodyObserver.observe($root.find('.vas-177-table-body')[0]);
        }

        function recalc() {
            var body = $root.find('.vas-177-table-body')[0];
            if (!body) { return; }
            var fs = parseFloat(window.getComputedStyle($root[0]).fontSize) || 16;
            var rowH = fs * (0.75 * 1.5 + 1.25) + 1;
            var headH = fs * (0.6875 * 1.5 + 1) + 1;
            var rows = Math.max(2, Math.floor((body.clientHeight - headH) / rowH));
            var cols = Math.max(2, Math.floor((body.clientWidth - fs * 5) / (fs * 4.5)));
            if (rows !== maxRows || cols !== maxCols) { maxRows = rows; maxCols = cols; render(); }
        }

        function topKeys(field, limit) {
            var tot = {};
            cells.forEach(function (c) { tot[c[field]] = (tot[c[field]] || 0) + Number(c.Qty || 0); });
            var all = Object.keys(tot).sort(function (a, b) { return tot[b] - tot[a]; });
            return { shown: all.slice(0, limit), total: all.length };
        }

        function render() {
            var $body = $root.find('.vas-177-table-body');
            var $footer = $root.find('.vas-177-footer');

            if (!loaded || !cells.length) {
                var title = !loaded ? lbl('VAS_177_Loading', 'Loading…') : lbl('VAS_177_Empty', 'No locator transfers for this period');
                var sub = !loaded ? '' : '<div class="vas-177-empty-sub">' + escapeHtml(lbl('VAS_177_EmptySub', 'Try a different month or year.')) + '</div>';
                $body.html('<div class="vas-177-empty"><div class="vas-177-empty-title">' + escapeHtml(title) + '</div>' + sub + '</div>');
                $footer.empty();
                return;
            }

            var rowSet = topKeys('FromLocator', maxRows);
            var colSet = topKeys('ToLocator', maxCols);
            var rows = rowSet.shown;
            var cols = colSet.shown;
            var map = {};
            var max = 0;
            cells.forEach(function (c) {
                map[c.FromLocator + KEY_SEP + c.ToLocator] = c;
                if (rows.indexOf(c.FromLocator) !== -1 && cols.indexOf(c.ToLocator) !== -1) {
                    max = Math.max(max, Number(c.Qty || 0));
                }
            });

            var tpl = 'minmax(0,1.3fr) repeat(' + cols.length + ',minmax(0,1fr))';
            var html = '<div class="vas-177-table-header" style="grid-template-columns:' + tpl + '">' +
                '<span class="vas-177-th" title="' + escapeHtml(lbl('VAS_177_FromTo', 'From / To')) + '">' + escapeHtml(lbl('VAS_177_FromTo', 'From / To')) + '</span>';
            cols.forEach(function (c) { html += '<span class="vas-177-th vas-177-right" title="' + escapeHtml(c) + '">' + escapeHtml(c) + '</span>'; });
            html += '</div>';

            rows.forEach(function (r) {
                html += '<div class="vas-177-row" style="grid-template-columns:' + tpl + '">' +
                    '<span class="vas-177-cell vas-177-cell--primary" title="' + escapeHtml(r) + '">' + escapeHtml(r) + '</span>';
                cols.forEach(function (c) {
                    var cell = map[r + KEY_SEP + c];
                    if (!cell) { html += '<span class="vas-177-cell vas-177-right vas-177-cell--none" title="">–</span>'; return; }
                    var q = Number(cell.Qty || 0);
                    var alpha = max > 0 ? (0.08 + 0.34 * (q / max)) : 0.08;
                    var tip = r + ' → ' + c + ': ' + formatQty(q) + ' (' + cell.Docs + ' ' + lbl('VAS_177_Docs', 'documents') + ')';
                    html += '<span class="vas-177-cell vas-177-right vas-177-cell--heat" style="background-color:rgba(0,131,218,' + alpha.toFixed(2) + ')" title="' + escapeHtml(tip) + '">' + escapeHtml(formatQty(q)) + '</span>';
                });
                html += '</div>';
            });
            $body.html(html);

            $footer.html('<span class="vas-177-footer-helper">' +
                escapeHtml(lbl('VAS_177_Showing', 'Showing') + ' ' + rows.length + ' ' + lbl('VAS_177_Of', 'of') + ' ' + rowSet.total + ' ' +
                    lbl('VAS_177_FromLocators', 'from-locators') + ' · ' + cols.length + ' ' + lbl('VAS_177_Of', 'of') + ' ' + colSet.total + ' ' +
                    lbl('VAS_177_ToLocators', 'to-locators')) + '</span>');
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
                    cells = (data && !data.error) ? (data.cells || []) : [];
                    render();
                    recalc();
                },
                error: function () {
                    loaded = true;
                    cells = [];
                    render();
                    $root.find('.vas-177-empty-title').text(lbl('VAS_177_LoadError', 'Unable to load data'));
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
            $(document).off('click.vas177');
            if (widgetObserver && $wrapper[0]) {
                widgetObserver.unobserve($wrapper[0]);
                widgetObserver = null;
            }
            if (bodyObserver) { bodyObserver.disconnect(); bodyObserver = null; }
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
