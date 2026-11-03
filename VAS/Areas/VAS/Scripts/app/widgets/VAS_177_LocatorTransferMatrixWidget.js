/**
 * Locator Transfer Matrix Widget (Material Transfer Dashboard)
 * Purpose - Heat-map matrix of stock moved between locators (from-locator rows x to-locator
 *           columns) for the selected month/year. Locators are labelled by name. The number of
 *           rows/columns adapts to the widget size (no inner scrollbar): from-locators are paged
 *           from the footer pager; to-locators beyond the columns that fit (max 10) are rolled
 *           up into one "Others" column so row totals stay complete.
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

    /* Falls back to the English text when the AD_Message key is not defined (getMsg echoes the key or [key]). */
    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t !== key && t !== '[' + key + ']') ? t : fallback;
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
        var maxRows = 1;
        var maxCols = 3;
        var page = 1;
        var MAX_COLS_CAP = 10;

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
                    /* Native month / year selects, same as the standard widgets (VAS_145): the browser
                       list is never clipped by the card, so the months show without a scroller. */
                    '<div class="vas-177-filters">' +
                        '<select class="vas-177-select vas-177-select--month" aria-label="' + escapeHtml(lbl('VAS_177_FilterByMonth', 'Filter by month')) + '"></select>' +
                        '<select class="vas-177-select vas-177-select--year" aria-label="' + escapeHtml(lbl('VAS_177_FilterByYear', 'Filter by year')) + '"></select>' +
                    '</div>' +
                '</div>' +
                '<div class="vas-177-table-wrap"><div class="vas-177-table-body"></div></div>' +
                '<div class="vas-177-footer"></div>'
            );
            $wrapper.empty().append($root);

            renderFilterOptions();
            $root.on('change', '.vas-177-select--month', function () {
                selMonth = parseInt($(this).val(), 10) || selMonth;
                load();
            });
            $root.on('change', '.vas-177-select--year', function () {
                selYear = parseInt($(this).val(), 10) || selYear;
                renderMonthOptions();
                load();
            });
            $root.on('click', '.vas-177-pager-btn', function () {
                page = Math.max(1, page + parseInt($(this).attr('data-dir'), 10));
                render();
            });

            setupObservers();
        }

        function monthName(m) {
            return new Date(2000, m - 1, 1).toLocaleDateString(window.navigator.language, { month: 'long' });
        }

        /* Years: the current year and the four before it. Months: see renderMonthOptions. */
        function renderFilterOptions() {
            var yHtml = '';
            for (var y = now.getFullYear(); y >= now.getFullYear() - 4; y--) {
                yHtml += '<option value="' + y + '">' + y + '</option>';
            }
            $root.find('.vas-177-select--year').html(yHtml).val(String(selYear));
            renderMonthOptions();
        }

        /* Book1 rows 52/59 (Sr. 37/48, default pending confirmation): the current year lists
           January to the current month only - months that have not happened are not offered -
           and a past year lists all twelve. No "All Months" entry (the matrix is always one
           month). The list is short enough to open without a scroller. */
        function renderMonthOptions() {
            var lastMonth = selYear === now.getFullYear() ? now.getMonth() + 1 : 12;
            if (selMonth > lastMonth) { selMonth = lastMonth; }
            var mHtml = '';
            for (var m = 1; m <= lastMonth; m++) {
                mHtml += '<option value="' + m + '">' + escapeHtml(monthName(m)) + '</option>';
            }
            $root.find('.vas-177-select--month').html(mHtml).val(String(selMonth));
        }

        function setupObservers() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) { $root[0].style.setProperty('--widget-inline-size', width + 'px'); }
                    var height = entries[i].contentRect.height;
                    if (height > 0) { $root[0].style.setProperty('--widget-block-size', height + 'px'); }
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
            var rows = Math.max(1, Math.floor((body.clientHeight - headH) / rowH));
            var cols = Math.min(MAX_COLS_CAP, Math.max(2, Math.floor((body.clientWidth - fs * 5) / (fs * 4.5))));
            if (rows !== maxRows || cols !== maxCols) { maxRows = rows; maxCols = cols; render(); }
        }

        /* All distinct locators on one axis, busiest first: [{ id, name }] */
        function axis(idField, nameField) {
            var tot = {};
            var name = {};
            cells.forEach(function (c) {
                var id = c[idField];
                tot[id] = (tot[id] || 0) + Number(c.Qty || 0);
                name[id] = c[nameField];
            });
            return Object.keys(tot).sort(function (a, b) { return tot[b] - tot[a]; })
                .map(function (id) { return { id: id, name: name[id] }; });
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

            var allRows = axis('FromID', 'FromLocator');
            var allCols = axis('ToID', 'ToLocator');

            /* Columns: those that fit; the rest are summed into one "Others" column. */
            var cols = allCols;
            var others = [];
            if (allCols.length > maxCols) {
                cols = allCols.slice(0, Math.max(1, maxCols - 1));
                others = allCols.slice(cols.length);
            }
            var otherIds = {};
            others.forEach(function (o) { otherIds[o.id] = o.name; });

            var totalPages = Math.max(1, Math.ceil(allRows.length / maxRows));
            if (page > totalPages) { page = totalPages; }
            var start = (page - 1) * maxRows;
            var rows = allRows.slice(start, start + maxRows);

            var map = {};
            cells.forEach(function (c) {
                var colKey = otherIds.hasOwnProperty(c.ToID) ? 'others' : c.ToID;
                var k = c.FromID + KEY_SEP + colKey;
                if (!map[k]) { map[k] = { Qty: 0, Docs: 0, names: [] }; }
                map[k].Qty += Number(c.Qty || 0);
                map[k].Docs += Number(c.Docs || 0);
                if (colKey === 'others') { map[k].names.push(c.ToLocator); }
            });

            var max = 0;
            rows.forEach(function (r) {
                Object.keys(map).forEach(function (k) {
                    if (k.indexOf(r.id + KEY_SEP) === 0) { max = Math.max(max, map[k].Qty); }
                });
            });

            var colList = cols.map(function (c) { return { key: c.id, name: c.name }; });
            if (others.length) { colList.push({ key: 'others', name: lbl('VAS_177_Others', 'Others') + ' (' + others.length + ')' }); }

            var tpl = 'minmax(0,1.3fr) repeat(' + colList.length + ',minmax(0,1fr))';
            var html = '<div class="vas-177-table-header" style="grid-template-columns:' + tpl + '">' +
                '<span class="vas-177-th" title="' + escapeHtml(lbl('VAS_177_FromTo', 'From / To')) + '">' + escapeHtml(lbl('VAS_177_FromTo', 'From / To')) + '</span>';
            colList.forEach(function (c) {
                var tip = c.key === 'others' ? others.map(function (o) { return o.name; }).join(', ') : c.name;
                html += '<span class="vas-177-th vas-177-right" title="' + escapeHtml(tip) + '">' + escapeHtml(c.name) + '</span>';
            });
            html += '</div>';

            rows.forEach(function (r) {
                html += '<div class="vas-177-row" style="grid-template-columns:' + tpl + '">' +
                    '<span class="vas-177-cell vas-177-cell--primary" title="' + escapeHtml(r.name) + '">' + escapeHtml(r.name) + '</span>';
                colList.forEach(function (c) {
                    var cell = map[r.id + KEY_SEP + c.key];
                    if (!cell) { html += '<span class="vas-177-cell vas-177-right vas-177-cell--none">–</span>'; return; }
                    var alpha = max > 0 ? (0.08 + 0.34 * (cell.Qty / max)) : 0.08;
                    var colName = c.key === 'others' ? cell.names.join(', ') : c.name;
                    var tip = r.name + ' → ' + colName + ': ' + formatQty(cell.Qty) + ' (' + cell.Docs + ' ' + lbl('VAS_177_Docs', 'documents') + ')';
                    html += '<span class="vas-177-cell vas-177-right vas-177-cell--heat" style="background-color:rgba(0,131,218,' + alpha.toFixed(2) + ')" title="' + escapeHtml(tip) + '">' + escapeHtml(formatQty(cell.Qty)) + '</span>';
                });
                html += '</div>';
            });
            $body.html(html);

            var helper = lbl('VAS_177_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + rows.length) + ' ' + lbl('VAS_177_Of', 'of') + ' ' + allRows.length + ' ' +
                lbl('VAS_177_FromLocators', 'from-locators') + ' · ' + allCols.length + ' ' + lbl('VAS_177_ToLocators', 'to-locators');
            var pager = '';
            if (allRows.length > maxRows) {
                pager = '<span class="vas-177-pager">' +
                    '<button type="button" class="vas-177-pager-btn" data-dir="-1"' + (page <= 1 ? ' disabled' : '') + '><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg></button>' +
                    '<span class="vas-177-pager-label">' + page + ' ' + escapeHtml(lbl('VAS_177_Of', 'of')) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-177-pager-btn" data-dir="1"' + (page >= totalPages ? ' disabled' : '') + '><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg></button>' +
                    '</span>';
            }
            $footer.html('<span class="vas-177-footer-helper" title="' + escapeHtml(helper) + '">' + escapeHtml(helper) + '</span>' + pager);
        }

        function load() {
            loaded = false;
            page = 1;
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
