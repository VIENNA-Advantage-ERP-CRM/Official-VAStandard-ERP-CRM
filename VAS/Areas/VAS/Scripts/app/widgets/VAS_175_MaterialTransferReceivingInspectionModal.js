/**
 * Transfer Receiving Inspection Widget (Material Transfer Dashboard)
 * Purpose - Quality results on received transfers. Table with search and paging.
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

    VAS.VAS_175_MaterialTransferReceivingInspectionModal = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-175-container">');
        var $root = $('<div class="vas-175-root">');
        var widgetObserver = null;

        var allRecords = [];
        var filtered = [];
        var PAGE_SIZE = 3;
        var page = 1;
        var searchText = '';
        var selMonth = 0;
        var selYear = 0;
        var loaded = false;

        var COLS = ["Transfer", "Product", "Parameter", "Range", "Actual", "Result"];
        var SEARCH_FIELDS = ["TransferNo", "ConfirmNo", "ProductName", "ParameterName"];
        var DATE_FIELD = 'InspectionDate';

        function rowCells(r) {
            var range = (Number(r.MinValue) || Number(r.MaxValue)) ? (r.MinValue + ' – ' + r.MaxValue) : '—';
            var res = r.Result || 'Pending';
            var pill = '<span class="vas-175-pill vas-175-pill--' + res.toLowerCase() + '">' + escapeHtml(lbl('VAS_175_Result' + res, res)) + '</span>';
            return [escapeHtml(r.TransferNo || r.ConfirmNo), escapeHtml(r.ProductName), escapeHtml(r.ParameterName), escapeHtml(range), escapeHtml(r.ActualValue || '—'), pill];
        }

        function build() {
            $root.html(
                '<div class="vas-175-header-row">' +
                    '<div class="vas-175-left-cluster">' +
                        '<div class="vas-175-icon-well"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 11l3 3L22 4"></path><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path></svg></div>' +
                        '<div class="vas-175-title-block">' +
                            '<div class="vas-175-title">' + escapeHtml(lbl('VAS_175_Title', 'Transfer Receiving Inspection')) + '</div>' +
                            '<div class="vas-175-subtitle">' + escapeHtml(lbl('VAS_175_Subtitle', 'Quality results on received transfers')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<span class="vas-175-count-pill" id="vas175-cnt">0</span>' +
                '</div>' +
                '<div class="vas-175-toolbar">' +
                    '<input type="search" class="vas-175-search" placeholder="' + escapeHtml(lbl('VAS_175_Search', 'Search')) + '" />' +
                    ''+
                '</div>' +
                '<div class="vas-175-table-wrap"><div class="vas-175-body"></div></div>' +
                '<div class="vas-175-pager"></div>'
            );
            $wrapper.empty().append($root);

            $root.on('input', '.vas-175-search', function () { searchText = $(this).val().toLowerCase(); page = 1; applyFilter(); });
            $root.on('change', '.vas-175-sel', function () {
                selMonth = parseInt($root.find('.vas-175-msel').val(), 10) || 0;
                selYear = parseInt($root.find('.vas-175-ysel').val(), 10) || 0;
                page = 1; applyFilter();
            });
            $root.on('click', '.vas-175-pg', function () {
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
            var $body = $root.find('.vas-175-body');
            $root.find('#vas175-cnt').text(filtered.length + ' ' + lbl('VAS_175_CountWord', 'results'));

            if (!loaded) {
                $body.html('<div class="vas-175-empty">' + escapeHtml(lbl('VAS_175_Loading', 'Loading…')) + '</div>');
                $root.find('.vas-175-pager').empty();
                return;
            }
            if (!filtered.length) {
                $body.html('<div class="vas-175-empty">' + escapeHtml(lbl('VAS_175_Empty', 'No inspection results to show')) + '</div>');
                $root.find('.vas-175-pager').empty();
                return;
            }

            var total = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
            if (page > total) { page = total; }
            var start = (page - 1) * PAGE_SIZE;
            var slice = filtered.slice(start, start + PAGE_SIZE);

            var html = '<table class="vas-175-table"><thead><tr>';
            for (var c = 0; c < COLS.length; c++) {
                html += '<th>' + escapeHtml(lbl('VAS_175_Col' + COLS[c].replace(/[^A-Za-z]/g, ''), COLS[c])) + '</th>';
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

            $root.find('.vas-175-pager').html(
                '<button type="button" class="vas-175-pg" data-dir="-1"' + (page <= 1 ? ' disabled' : '') + '>‹</button>' +
                '<span class="vas-175-pager-label">' + page + ' ' + escapeHtml(lbl('VAS_175_Of', 'of')) + ' ' + total + '</span>' +
                '<button type="button" class="vas-175-pg" data-dir="1"' + (page >= total ? ' disabled' : '') + '>›</button>'
            );
        }

        function fillYears() {
            if (!$root.find('.vas-175-ysel').length) { return; }
            var years = {};
            allRecords.forEach(function (r) { var d = new Date(r[DATE_FIELD]); if (!isNaN(d)) { years[d.getUTCFullYear()] = 1; } });
            var list = Object.keys(years).sort().reverse();
            var html = '<option value="0">' + escapeHtml(lbl('VAS_175_AllYears', 'All Years')) + '</option>';
            list.forEach(function (y) { html += '<option value="' + y + '">' + y + '</option>'; });
            $root.find('.vas-175-ysel').html(html);
        }

        function showLoadError() {
            loaded = true;
            $root.find('.vas-175-body').html('<div class="vas-175-empty">' + escapeHtml(lbl('VAS_175_LoadError', 'Unable to load data')) + '</div>');
            $root.find('.vas-175-pager').empty();
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
