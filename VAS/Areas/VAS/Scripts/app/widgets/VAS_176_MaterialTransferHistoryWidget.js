/**
 * Material Transfer History Widget (Material Transfer Dashboard)
 * Purpose - Completed and closed transfers. Grid data widget: shared-template CSS grid table, adaptive row count,
 *           footer pager (no inner scrollbar). Follows the Onfinity dashboard-widgets spec
 *           and the sibling Material Transfer widgets (VAS_172-174).
 * Prefix  - VAS_176_
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

    VAS.VAS_176_MaterialTransferHistoryWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-176-container">');
        var $root = $('<div class="vas-176-root">');
        var widgetObserver = null;
        var bodyObserver = null;

        var allRecords = [];
        var filtered = [];
        var rowsPerPage = 1;
        var MIN_ROWS = 1;
        var page = 1;
        var HAS_FILTERS = true;
        var now = new Date();
        /* The running month and year are selected by default. */
        var selMonth = HAS_FILTERS ? now.getMonth() + 1 : 0;
        var selYear = HAS_FILTERS ? now.getFullYear() : 0;
        var loaded = false;
        var DATE_FIELD = 'MovementDate';

        /* Single column template shared by the header row and every body row. */
        var COLS = [{"label": "Document", "fr": 1.0}, {"label": "Date", "fr": 1.4}, {"label": "From / To", "fr": 2.2}, {"label": "Qty (In Base UOM)", "key": "VAS_176_ColQtyInBaseUOM", "fr": 1.0, "right": true}];
        var GRID = COLS.map(function (c) { return 'minmax(0,' + c.fr + 'fr)'; }).join(' ');

        function rowCells(r) {
            var route = (r.FromWarehouse || '—') + ' → ' + (r.ToWarehouse || '—');
            return [
                { t: r.DocumentNo || '—', primary: true, link: r.MovementID },
                { t: formatDate(r.MovementDate) },
                { t: route },
                { t: formatQty(r.TotalQty), title: formatQty(r.TotalQty) + ' (' + (r.LineCount || 0) + ' ' + lbl('VAS_176_Lines', 'lines') + ')' }
            ];
        }

        function headerPills() {
            return '';
        }

        function build() {
            $root.html(
                '<div class="vas-176-header-row">' +
                    '<div class="vas-176-left-cluster">' +
                        '<div class="vas-176-icon-well"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"></circle><polyline points="12 7 12 12 15 14"></polyline></svg></div>' +
                        '<div class="vas-176-title-block">' +
                            '<div class="vas-176-title" title="' + escapeHtml(lbl('VAS_176_Title', 'Material Transfer History')) + '">' + escapeHtml(lbl('VAS_176_Title', 'Material Transfer History')) + '</div>' +
                            '<div class="vas-176-subtitle" title="' + escapeHtml(lbl('VAS_176_SubtitleHistory', 'History of completed and closed material transfers')) + '">' + escapeHtml(lbl('VAS_176_SubtitleHistory', 'History of completed and closed material transfers')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<span class="vas-176-pills"></span>' +
                    (HAS_FILTERS ?
                    /* Native month / year selects, same as the standard widgets (VAS_145): the browser
                       list is never clipped by the card, so every month shows without a scroller. */
                    '<div class="vas-176-filters">' +
                        '<select class="vas-176-select vas-176-select--month" aria-label="' + escapeHtml(lbl('VAS_176_FilterByMonth', 'Filter by month')) + '"></select>' +
                        '<select class="vas-176-select vas-176-select--year" aria-label="' + escapeHtml(lbl('VAS_176_FilterByYear', 'Filter by year')) + '"></select>' +
                    '</div>' : '') +
                '</div>' +
                '<div class="vas-176-table-wrap">' +
                    '<div class="vas-176-table-header"></div>' +
                    '<div class="vas-176-table-body"></div>' +
                '</div>' +
                '<div class="vas-176-footer"></div>'
            );
            $wrapper.empty().append($root);

            var hdr = '';
            COLS.forEach(function (c) {
                var t = lbl(c.key || ('VAS_176_Col' + c.label.replace(/[^A-Za-z]/g, '')), c.label);
                hdr += '<span class="vas-176-th' + (c.right ? ' vas-176-right' : '') + '" title="' + escapeHtml(t) + '">' + escapeHtml(t) + '</span>';
            });
            $root.find('.vas-176-table-header').css('grid-template-columns', GRID).html(hdr);

            renderFilterOptions();
            $root.on('change', '.vas-176-select--month', function () {
                selMonth = parseInt($(this).val(), 10) || selMonth;
                page = 1;
                applyFilter();
            });
            $root.on('change', '.vas-176-select--year', function () {
                selYear = parseInt($(this).val(), 10) || selYear;
                clampFutureMonths();
                page = 1;
                applyFilter();
            });
            $root.on('click', '.vas-176-doc-link', function (e) {
                e.preventDefault();
                e.stopPropagation();
                openTransfer($(this).attr('data-id'));
            });
            $root.on('click', '.vas-176-pager-btn', function () {
                var dir = parseInt($(this).attr('data-dir'), 10);
                var total = Math.max(1, Math.ceil(filtered.length / rowsPerPage));
                page = Math.min(total, Math.max(1, page + dir));
                render();
            });

            setupObservers();
        }

        /* Shows the full subtitle as a tooltip only when it is clipped (ellipsis). */
        function updateSubtitleTip() {
            var el = $root.find('.vas-176-subtitle')[0];
            if (!el) { return; }
            var full = lbl('VAS_176_SubtitleHistory', 'History of completed and closed material transfers');
            if (el.scrollWidth > el.clientWidth) { el.setAttribute('title', full); } else { el.removeAttribute('title'); }
        }

        /* Opens the transfer. Hosted on the Material Transfer window (windowNo >= 0) the window's own
           grid is navigated to the record (same pattern as InvoicesWidget / VAS_227); on a home
           dashboard the Material Transfer window is opened on it. */
        function openTransfer(id) {
            id = Number(id);
            if (!id) { return; }
            try {
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": "M_Movement.M_Movement_ID=" + id,
                        "TabLayout": "Y",
                        "TabIndex": "0"
                    });
                } else {
                    VAS.ZoomUtil.zoomToRecord('M_Movement_ID', id, 0, 'VAS_MaterialTransfer', 'Material Transfer');
                }
            } catch (e) { }
        }

        function monthName(m) {
            return new Date(2000, m - 1, 1).toLocaleDateString(window.navigator.language, { month: 'long' });
        }

        /* Fills the month / year selects (years = current year + the years present in the data). */
        function renderFilterOptions() {
            if (!HAS_FILTERS) { return; }
            var mHtml = '';
            for (var m = 1; m <= 12; m++) {
                mHtml += '<option value="' + m + '">' + escapeHtml(monthName(m)) + '</option>';
            }
            $root.find('.vas-176-select--month').html(mHtml).val(String(selMonth));

            var yHtml = '';
            years().forEach(function (y) { yHtml += '<option value="' + y + '">' + y + '</option>'; });
            $root.find('.vas-176-select--year').html(yHtml).val(String(selYear));
            clampFutureMonths();
        }

        /* Future months are unselectable while the current year is chosen (as in VAS_145). */
        function clampFutureMonths() {
            var curYear = new Date().getFullYear();
            var curMonth = new Date().getMonth() + 1;
            var isCurrentYear = selYear === curYear;
            var $m = $root.find('.vas-176-select--month');
            $m.find('option').each(function () {
                $(this).prop('disabled', isCurrentYear && Number($(this).val()) > curMonth);
            });
            if (isCurrentYear && selMonth > curMonth) {
                selMonth = curMonth;
                $m.val(String(selMonth));
            }
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
                updateSubtitleTip();
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) { $root[0].style.setProperty('--widget-inline-size', width + 'px'); }
                    var height = entries[i].contentRect.height;
                    if (height > 0) { $root[0].style.setProperty('--widget-block-size', height + 'px'); }
                }
            });
            widgetObserver.observe($wrapper[0]);

            /* Adaptive row count: rows that fit the body = floor(height / row height). */
            bodyObserver = new ResizeObserver(function () { recalcRows(); updateSubtitleTip(); });
            bodyObserver.observe($root.find('.vas-176-table-body')[0]);
        }

        function recalcRows() {
            var body = $root.find('.vas-176-table-body')[0];
            if (!body) { return; }
            var fs = parseFloat(window.getComputedStyle($root[0]).fontSize) || 16;
            var rowH = fs * (0.75 * 1.5 + 1.25) + 1;
            var n = Math.max(MIN_ROWS, Math.floor(body.clientHeight / rowH));
            if (n !== rowsPerPage) { rowsPerPage = n; render(); }
        }

        function applyFilter() {
            filtered = allRecords.filter(function (r) {
                var d = ymd(r[DATE_FIELD]);
                if (!d) { return false; }
                if (selYear && d.y !== selYear) { return false; }
                if (selMonth && d.m !== selMonth) { return false; }
                return true;
            });
            render();
        }

        function render() {
            var $body = $root.find('.vas-176-table-body');
            var $footer = $root.find('.vas-176-footer');
            $root.find('.vas-176-pills').html(loaded ? headerPills() : '');

            if (!loaded) {
                $body.html('<div class="vas-176-empty"><div class="vas-176-empty-title">' + escapeHtml(lbl('VAS_176_Loading', 'Loading…')) + '</div></div>');
                $footer.empty();
                return;
            }
            if (!filtered.length) {
                $body.html('<div class="vas-176-empty"><div class="vas-176-empty-title">' + escapeHtml(lbl('VAS_176_Empty', 'No completed transfers for this period')) + '</div>' +
                    '<div class="vas-176-empty-sub">' + escapeHtml(lbl('VAS_176_EmptySub', 'Try a different month or year.')) + '</div></div>');
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
                html += '<div class="vas-176-row" style="grid-template-columns:' + GRID + '">';
                for (var j = 0; j < cells.length; j++) {
                    var c = cells[j];
                    var cls = 'vas-176-cell' + (c.primary ? ' vas-176-cell--primary' : '') + (COLS[j].right ? ' vas-176-right' : '');
                    html += '<span class="' + cls + '" title="' + escapeHtml(c.title || c.t) + '">' + (c.link ? '<a class="vas-176-doc-link" href="javascript:void(0)" data-id="' + escapeHtml(c.link) + '">' + escapeHtml(c.t) + '</a>' : (c.html || escapeHtml(c.t))) + '</span>';
                }
                html += '</div>';
            }
            $body.html(html);
            updateSubtitleTip();

            if (filtered.length > rowsPerPage) {
                $footer.html(
                    '<span class="vas-176-footer-helper">' + escapeHtml(lbl('VAS_176_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_176_Of', 'of') + ' ' + filtered.length) + '</span>' +
                    '<span class="vas-176-pager">' +
                        '<button type="button" class="vas-176-pager-btn" data-dir="-1"' + (page <= 1 ? ' disabled' : '') + '><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg></button>' +
                        '<span class="vas-176-pager-label">' + page + ' ' + escapeHtml(lbl('VAS_176_Of', 'of')) + ' ' + total + '</span>' +
                        '<button type="button" class="vas-176-pager-btn" data-dir="1"' + (page >= total ? ' disabled' : '') + '><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg></button>' +
                    '</span>'
                );
            } else {
                $footer.html('<span class="vas-176-footer-helper">' + escapeHtml(lbl('VAS_176_Showing', 'Showing') + ' 1–' + filtered.length + ' ' + lbl('VAS_176_Of', 'of') + ' ' + filtered.length) + '</span>');
            }
        }

        function showLoadError() {
            loaded = true;
            allRecords = [];
            filtered = [];
            render();
            $root.find('.vas-176-empty-title').text(lbl('VAS_176_LoadError', 'Unable to load data'));
        }

        function load() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_176_MaterialTransferHistoryWidget/GetTransferHistory',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }
                    if (!data || data.error) { showLoadError(); return; }
                    loaded = true;
                    allRecords = data.records || [];
                    renderFilterOptions();
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

    VAS.VAS_176_MaterialTransferHistoryWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_176_MaterialTransferHistoryWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_176_MaterialTransferHistoryWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_176_MaterialTransferHistoryWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_176_MaterialTransferHistoryWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_176_MaterialTransferHistoryWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
