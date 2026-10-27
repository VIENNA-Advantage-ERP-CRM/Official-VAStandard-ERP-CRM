/**
 * Transfer Receiving Inspection Widget (Material Transfer Dashboard)
 * Purpose - Quality results on received transfers. Grid data widget: shared-template CSS grid table, adaptive row count,
 *           footer pager (no inner scrollbar). Follows the Onfinity dashboard-widgets spec
 *           and the sibling Material Transfer widgets (VAS_172-174).
 * Prefix  - VAS_175_
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

    /* Dates arrive as "YYYY-MM-DDT00:00:00" without a time zone: read the calendar date as written
       (UTC getters would show the previous day in time zones ahead of UTC). */
    function ymd(iso) {
        var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso || ''));
        if (m) { return { y: +m[1], m: +m[2], d: +m[3] }; }
        var d = new Date(iso);
        return isNaN(d) ? null : { y: d.getFullYear(), m: d.getMonth() + 1, d: d.getDate() };
    }

    function formatDate(iso) {
        var p = ymd(iso);
        return p ? (p.d + ' ' + MONTHS[p.m - 1] + ' ' + p.y) : '—';
    }

    function formatQty(v) {
        var n = Number(v || 0);
        return n.toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
    }

    VAS.VAS_175_MaterialTransferReceivingInspectionModal = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-175-container">');
        var $root = $('<div class="vas-175-root">');
        var widgetObserver = null;
        var bodyObserver = null;

        var allRecords = [];
        var filtered = [];
        var rowsPerPage = 1;
        var MIN_ROWS = 1;
        var page = 1;
        var HAS_FILTERS = false;
        var now = new Date();
        /* The running month and year are selected by default. */
        var selMonth = HAS_FILTERS ? now.getMonth() + 1 : 0;
        var selYear = HAS_FILTERS ? now.getFullYear() : 0;
        var loaded = false;
        var openMenu = '';
        var DATE_FIELD = 'InspectionDate';

        /* Single column template shared by the header row and every body row. */
        var COLS = [{"label": "Transfer", "fr": 1.0}, {"label": "Product", "fr": 1.3}, {"label": "Parameter", "fr": 1.2}, {"label": "Actual", "fr": 0.7, "right": true}, {"label": "Result", "fr": 1.5}];
        var GRID = COLS.map(function (c) { return 'minmax(0,' + c.fr + 'fr)'; }).join(' ');

        function rowCells(r) {
            var range = (Number(r.MinValue) || Number(r.MaxValue)) ? (r.MinValue + ' – ' + r.MaxValue) : '—';
            var res = r.Result || 'Pending';
            var resTxt = lbl('VAS_175_Result' + res, res);
            return [
                { t: r.TransferNo || r.ConfirmNo || '—', primary: true },
                { t: r.ProductName || '—' },
                { t: r.ParameterName || '—' },
                { t: r.ActualValue || '—', title: (lbl('VAS_175_Range', 'Range') + ': ' + range) },
                { t: resTxt, html: '<span class="vas-175-pill vas-175-pill--' + res.toLowerCase() + '">' + escapeHtml(resTxt) + '</span>' }
            ];
        }

        function headerPills() {
            var failed = filtered.filter(function (r) { return r.Result === 'Fail'; }).length;
            var html = '';
            if (failed) { html += '<span class="vas-175-stat vas-175-stat--danger">' + failed + ' ' + escapeHtml(lbl('VAS_175_Failed', 'failed')) + '</span>'; }
            return html;
        }

        function build() {
            $root.html(
                '<div class="vas-175-header-row">' +
                    '<div class="vas-175-left-cluster">' +
                        '<div class="vas-175-icon-well"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 11l3 3L22 4"></path><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path></svg></div>' +
                        '<div class="vas-175-title-block">' +
                            '<div class="vas-175-title" title="' + escapeHtml(lbl('VAS_175_Title', 'Transfer Receiving Inspection')) + '">' + escapeHtml(lbl('VAS_175_Title', 'Transfer Receiving Inspection')) + '</div>' +
                            '<div class="vas-175-subtitle">' + escapeHtml(lbl('VAS_175_Subtitle', 'Quality results on received transfers')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<span class="vas-175-pills"></span>' +
                    (HAS_FILTERS ?
                    '<div class="vas-175-filters">' +
                        '<div class="vas-175-dropdown" data-dd="m"><button type="button" class="vas-175-dropdown-btn"><span class="vas-175-dd-lbl">' + escapeHtml(MONTHS[selMonth - 1] || lbl('VAS_175_AllMonths', 'All Months')) + '</span></button></div>' +
                        '<div class="vas-175-dropdown" data-dd="y"><button type="button" class="vas-175-dropdown-btn"><span class="vas-175-dd-lbl">' + escapeHtml(selYear ? String(selYear) : lbl('VAS_175_AllYears', 'All Years')) + '</span></button></div>' +
                    '</div>' : '') +
                '</div>' +
                '<div class="vas-175-table-wrap">' +
                    '<div class="vas-175-table-header"></div>' +
                    '<div class="vas-175-table-body"></div>' +
                '</div>' +
                '<div class="vas-175-footer"></div>'
            );
            $wrapper.empty().append($root);

            var hdr = '';
            COLS.forEach(function (c) {
                var t = lbl('VAS_175_Col' + c.label.replace(/[^A-Za-z]/g, ''), c.label);
                hdr += '<span class="vas-175-th' + (c.right ? ' vas-175-right' : '') + '" title="' + escapeHtml(t) + '">' + escapeHtml(t) + '</span>';
            });
            $root.find('.vas-175-table-header').css('grid-template-columns', GRID).html(hdr);

            $root.on('click', '.vas-175-dropdown-btn', function (e) {
                e.stopPropagation();
                var key = $(this).closest('.vas-175-dropdown').attr('data-dd');
                toggleMenu(key);
            });
            $root.on('click', '.vas-175-dd-option', function (e) {
                e.stopPropagation();
                var key = $(this).closest('.vas-175-dropdown').attr('data-dd');
                var val = parseInt($(this).attr('data-v'), 10);
                if (key === 'm') { selMonth = val; } else { selYear = val; }
                $(this).closest('.vas-175-dropdown').find('.vas-175-dd-lbl').text(val === 0 ? (key === 'm' ? lbl('VAS_175_AllMonths', 'All Months') : lbl('VAS_175_AllYears', 'All Years')) : $(this).text());
                closeMenus();
                page = 1;
                applyFilter();
            });
            $(document).on('click.vas175', closeMenus);
            $root.on('click', '.vas-175-pager-btn', function () {
                var dir = parseInt($(this).attr('data-dir'), 10);
                var total = Math.max(1, Math.ceil(filtered.length / rowsPerPage));
                page = Math.min(total, Math.max(1, page + dir));
                render();
            });

            setupObservers();
        }

        function closeMenus() {
            openMenu = '';
            $root.find('.vas-175-dropdown-menu').remove();
        }

        function toggleMenu(key) {
            var wasOpen = (openMenu === key);
            closeMenus();
            if (wasOpen) { return; }
            openMenu = key;
            var html = '<div class="vas-175-dropdown-menu">';
            if (key === 'm') {
                html += '<div class="vas-175-dd-option' + (selMonth === 0 ? ' vas-175-dd-option--sel' : '') + '" data-v="0">' + escapeHtml(lbl('VAS_175_AllMonths', 'All Months')) + '</div>';
                MONTHS.forEach(function (m, i) {
                    html += '<div class="vas-175-dd-option' + (selMonth === i + 1 ? ' vas-175-dd-option--sel' : '') + '" data-v="' + (i + 1) + '">' + m + '</div>';
                });
            } else {
                html += '<div class="vas-175-dd-option' + (selYear === 0 ? ' vas-175-dd-option--sel' : '') + '" data-v="0">' + escapeHtml(lbl('VAS_175_AllYears', 'All Years')) + '</div>';
                years().forEach(function (y) {
                    html += '<div class="vas-175-dd-option' + (selYear === y ? ' vas-175-dd-option--sel' : '') + '" data-v="' + y + '">' + y + '</div>';
                });
            }
            html += '</div>';
            $root.find('.vas-175-dropdown[data-dd="' + key + '"]').append(html);
        }

        function years() {
            var set = {};
            set[new Date().getFullYear()] = 1;
            allRecords.forEach(function (r) { var d = ymd(r[DATE_FIELD]); if (d) { set[d.y] = 1; } });
            return Object.keys(set).map(Number).sort(function (a, b) { return b - a; });
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

            /* Adaptive row count: rows that fit the body = floor(height / row height). */
            bodyObserver = new ResizeObserver(function () { recalcRows(); });
            bodyObserver.observe($root.find('.vas-175-table-body')[0]);
        }

        function recalcRows() {
            var body = $root.find('.vas-175-table-body')[0];
            if (!body) { return; }
            var fs = parseFloat(window.getComputedStyle($root[0]).fontSize) || 16;
            var rowH = fs * (0.75 * 1.5 + 1.25) + 1;
            var n = Math.max(MIN_ROWS, Math.floor(body.clientHeight / rowH));
            if (n !== rowsPerPage) { rowsPerPage = n; render(); }
        }

        function applyFilter() {
            filtered = allRecords.filter(function (r) {
                if (!selYear && !selMonth) { return true; }
                var d = ymd(r[DATE_FIELD]);
                if (!d) { return false; }
                if (selYear && d.y !== selYear) { return false; }
                if (selMonth && d.m !== selMonth) { return false; }
                return true;
            });
            render();
        }

        function render() {
            var $body = $root.find('.vas-175-table-body');
            var $footer = $root.find('.vas-175-footer');
            $root.find('.vas-175-pills').html(loaded ? headerPills() : '');

            if (!loaded) {
                $body.html('<div class="vas-175-empty"><div class="vas-175-empty-title">' + escapeHtml(lbl('VAS_175_Loading', 'Loading…')) + '</div></div>');
                $footer.empty();
                return;
            }
            if (!filtered.length) {
                $body.html('<div class="vas-175-empty"><div class="vas-175-empty-title">' + escapeHtml(lbl('VAS_175_Empty', 'No inspection results yet')) + '</div>' +
                    '<div class="vas-175-empty-sub">' + escapeHtml(lbl('VAS_175_EmptySub', 'Results appear once a transfer confirmation is inspected.')) + '</div></div>');
                $footer.empty();
                return;
            }

            var total = Math.max(1, Math.ceil(filtered.length / rowsPerPage));
            if (page > total) { page = total; }
            var start = (page - 1) * rowsPerPage;
            var end = Math.min(start + rowsPerPage, filtered.length);

            var html = '';
            for (var i = start; i < end; i++) {
                var cells = rowCells(filtered[i]);
                html += '<div class="vas-175-row" style="grid-template-columns:' + GRID + '">';
                for (var j = 0; j < cells.length; j++) {
                    var c = cells[j];
                    var cls = 'vas-175-cell' + (c.primary ? ' vas-175-cell--primary' : '') + (COLS[j].right ? ' vas-175-right' : '');
                    html += '<span class="' + cls + '" title="' + escapeHtml(c.title || c.t) + '">' + (c.html || escapeHtml(c.t)) + '</span>';
                }
                html += '</div>';
            }
            $body.html(html);

            if (filtered.length > rowsPerPage) {
                $footer.html(
                    '<span class="vas-175-footer-helper">' + escapeHtml(lbl('VAS_175_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_175_Of', 'of') + ' ' + filtered.length) + '</span>' +
                    '<span class="vas-175-pager">' +
                        '<button type="button" class="vas-175-pager-btn" data-dir="-1"' + (page <= 1 ? ' disabled' : '') + '><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg></button>' +
                        '<span class="vas-175-pager-label">' + page + ' ' + escapeHtml(lbl('VAS_175_Of', 'of')) + ' ' + total + '</span>' +
                        '<button type="button" class="vas-175-pager-btn" data-dir="1"' + (page >= total ? ' disabled' : '') + '><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg></button>' +
                    '</span>'
                );
            } else {
                $footer.html('<span class="vas-175-footer-helper">' + escapeHtml(lbl('VAS_175_Showing', 'Showing') + ' 1–' + filtered.length + ' ' + lbl('VAS_175_Of', 'of') + ' ' + filtered.length) + '</span>');
            }
        }

        function showLoadError() {
            loaded = true;
            allRecords = [];
            filtered = [];
            render();
            $root.find('.vas-175-empty-title').text(lbl('VAS_175_LoadError', 'Unable to load data'));
        }

        function load() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_175_MaterialTransferReceivingInspection/GetInspections',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }
                    if (!data || data.error) { showLoadError(); return; }
                    loaded = true;
                    allRecords = data.rows || [];
                    applyFilter();
                    recalcRows();
                },
                error: function () { showLoadError(); }
            });
        }

        this.Initalize = function () {
            build();
            render();
            load();
        };

        this.refreshWidget = function () { loaded = false; render(); load(); };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            $(document).off('click.vas175');
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

    VAS.VAS_175_MaterialTransferReceivingInspectionModal.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_175_MaterialTransferReceivingInspectionModal.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_175_MaterialTransferReceivingInspectionModal.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_175_MaterialTransferReceivingInspectionModal.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_175_MaterialTransferReceivingInspectionModal.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_175_MaterialTransferReceivingInspectionModal.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
