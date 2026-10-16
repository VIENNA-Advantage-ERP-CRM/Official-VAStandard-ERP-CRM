/**
 * Material Transfer History Widget (Material Transfer Dashboard)
 * Purpose - Completed and closed transfers. Table with search and month/year filter and paging.
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

    function formatDate(iso) {
        if (!iso) { return '—'; }
        var d = new Date(iso);
        if (isNaN(d)) { return iso; }
        return d.getUTCDate() + ' ' + MONTHS[d.getUTCMonth()] + ' ' + d.getUTCFullYear();
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

        var allRecords = [];
        var filtered = [];
        var PAGE_SIZE = 3;
        var page = 1;
        var searchText = '';
        var selMonth = 0;
        var selYear = 0;
        var loaded = false;

        var COLS = ["Document", "Date", "From / To", "Lines", "Qty"];
        var SEARCH_FIELDS = ["DocumentNo", "FromWarehouse", "ToWarehouse"];
        var DATE_FIELD = 'MovementDate';

        function rowCells(r) {
            return [escapeHtml(r.DocumentNo), escapeHtml(formatDate(r.MovementDate)), escapeHtml((r.FromWarehouse || '—') + ' → ' + (r.ToWarehouse || '—')), escapeHtml(r.LineCount), escapeHtml(formatQty(r.TotalQty))];
        }

        function build() {
            $root.html(
                '<div class="vas-176-header-row">' +
                    '<div class="vas-176-left-cluster">' +
                        '<div class="vas-176-icon-well"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"></circle><polyline points="12 7 12 12 15 14"></polyline></svg></div>' +
                        '<div class="vas-176-title-block">' +
                            '<div class="vas-176-title">' + escapeHtml(lbl('VAS_176_Title', 'Material Transfer History')) + '</div>' +
                            '<div class="vas-176-subtitle">' + escapeHtml(lbl('VAS_176_Subtitle', 'Completed and closed transfers')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<span class="vas-176-count-pill" id="vas176-cnt">0</span>' +
                '</div>' +
                '<div class="vas-176-toolbar">' +
                    '<input type="search" class="vas-176-search" placeholder="' + escapeHtml(lbl('VAS_176_Search', 'Search')) + '" />' +
                    '<select class="vas-176-sel vas-176-msel"><option value="0">' + escapeHtml(lbl('VAS_176_AllMonths', 'All Months')) + '</option>' + MONTHS.map(function (m, i) { return '<option value="' + (i + 1) + '">' + m + '</option>'; }).join('') + '</select>' +
                    '<select class="vas-176-sel vas-176-ysel"></select>' +
                '</div>' +
                '<div class="vas-176-table-wrap"><div class="vas-176-body"></div></div>' +
                '<div class="vas-176-pager"></div>'
            );
            $wrapper.empty().append($root);

            $root.on('input', '.vas-176-search', function () { searchText = $(this).val().toLowerCase(); page = 1; applyFilter(); });
            $root.on('change', '.vas-176-sel', function () {
                selMonth = parseInt($root.find('.vas-176-msel').val(), 10) || 0;
                selYear = parseInt($root.find('.vas-176-ysel').val(), 10) || 0;
                page = 1; applyFilter();
            });
            $root.on('click', '.vas-176-pg', function () {
                var dir = parseInt($(this).attr('data-dir'), 10);
                var total = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
                page = Math.min(total, Math.max(1, page + dir));
                render();
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

        function applyFilter() {
            filtered = allRecords.filter(function (r) {
                if (searchText) {
                    var hit = false;
                    for (var i = 0; i < SEARCH_FIELDS.length; i++) {
                        if (String(r[SEARCH_FIELDS[i]] || '').toLowerCase().indexOf(searchText) !== -1) { hit = true; break; }
                    }
                    if (!hit) { return false; }
                }
                if (selYear || selMonth) {
                    var d = new Date(r[DATE_FIELD]);
                    if (isNaN(d)) { return false; }
                    if (selYear && d.getUTCFullYear() !== selYear) { return false; }
                    if (selMonth && (d.getUTCMonth() + 1) !== selMonth) { return false; }
                }
                return true;
            });
            render();
        }

        function render() {
            var $body = $root.find('.vas-176-body');
            $root.find('#vas176-cnt').text(filtered.length + ' ' + lbl('VAS_176_CountWord', 'transfers'));

            if (!loaded) {
                $body.html('<div class="vas-176-empty">' + escapeHtml(lbl('VAS_176_Loading', 'Loading…')) + '</div>');
                $root.find('.vas-176-pager').empty();
                return;
            }
            if (!filtered.length) {
                $body.html('<div class="vas-176-empty">' + escapeHtml(lbl('VAS_176_Empty', 'No completed transfers found')) + '</div>');
                $root.find('.vas-176-pager').empty();
                return;
            }

            var total = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
            if (page > total) { page = total; }
            var start = (page - 1) * PAGE_SIZE;
            var slice = filtered.slice(start, start + PAGE_SIZE);

            var html = '<table class="vas-176-table"><thead><tr>';
            for (var c = 0; c < COLS.length; c++) {
                html += '<th>' + escapeHtml(lbl('VAS_176_Col' + COLS[c].replace(/[^A-Za-z]/g, ''), COLS[c])) + '</th>';
            }
            html += '</tr></thead><tbody>';
            for (var i = 0; i < slice.length; i++) {
                var cells = rowCells(slice[i]);
                html += '<tr>';
                for (var j = 0; j < cells.length; j++) { html += '<td>' + cells[j] + '</td>'; }
                html += '</tr>';
            }
            html += '</tbody></table>';
            $body.html(html);

            $root.find('.vas-176-pager').html(
                '<button type="button" class="vas-176-pg" data-dir="-1"' + (page <= 1 ? ' disabled' : '') + '>‹</button>' +
                '<span class="vas-176-pager-label">' + page + ' ' + escapeHtml(lbl('VAS_176_Of', 'of')) + ' ' + total + '</span>' +
                '<button type="button" class="vas-176-pg" data-dir="1"' + (page >= total ? ' disabled' : '') + '>›</button>'
            );
        }

        function fillYears() {
            if (!$root.find('.vas-176-ysel').length) { return; }
            var years = {};
            allRecords.forEach(function (r) { var d = new Date(r[DATE_FIELD]); if (!isNaN(d)) { years[d.getUTCFullYear()] = 1; } });
            var list = Object.keys(years).sort().reverse();
            var html = '<option value="0">' + escapeHtml(lbl('VAS_176_AllYears', 'All Years')) + '</option>';
            list.forEach(function (y) { html += '<option value="' + y + '">' + y + '</option>'; });
            $root.find('.vas-176-ysel').html(html);
        }

        function showLoadError() {
            loaded = true;
            $root.find('.vas-176-body').html('<div class="vas-176-empty">' + escapeHtml(lbl('VAS_176_LoadError', 'Unable to load data')) + '</div>');
            $root.find('.vas-176-pager').empty();
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
                    fillYears();
                    applyFilter();
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
