/**
 * VAS KPI Drill-Through — shared drill-down modal for the KPI tiles.
 * Purpose - One reusable, paged modal that lists the records contributing to a KPI
 *           tile's value (overdue schedules, open opportunities, customers, ...).
 *           Each KPI widget (VAS_122 / VAS_124 / VAS_125) creates one instance,
 *           makes its card clickable, and opens the modal on click; a row click
 *           routes to that record via the widget's navigate callback. Reuses the
 *           existing list endpoints (VAS_138 / VAS_139 / VAS_122) - no new list UI
 *           per widget. CSS namespaced MPC-kpidrill-* (MPC prefix rule).
 *
 * Usage   - var drill = VAS.KpiDrill({
 *               title: 'Overdue receivables',
 *               endpoint: 'VAS_138_DelayedPaymentsListWidget/GetRows',
 *               extraParams: {},                 // optional static query params
 *               pageSize: 7,                     // default 7
 *               mapData: function (data) { return { items, total, currency }; },
 *               mapRow: function (item, cur, h) { return { bpId, title, meta, valueText, valueTone }; },
 *               navigate: function (bpId) { ... }
 *           });
 *           drill.open(); drill.close(); drill.dispose();
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
    var AVATAR_COLORS = ['#1F83FF', '#5F4AA6', '#0B6B45', '#D78B10', '#0083DA', '#A33F3F'];

    function label(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return t && t.charAt(0) !== '[' ? t : fallback;
    }
    function escapeHtml(value) {
        if (value == null) { return ''; }
        return String(value).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function parseResponse(response) {
        var parsed = response;
        if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
        if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
        return parsed || {};
    }
    function formatCount(value) {
        var n = Number(value || 0);
        if (!isFinite(n)) { n = 0; }
        return Math.round(n).toLocaleString(window.navigator.language);
    }
    function usesIndian(iso) { return INDIAN_NUMBERING_CURRENCIES.indexOf(String(iso || '').toUpperCase()) >= 0; }
    function formatMoney(value, cur) {
        var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
        var sign = n < 0 ? '-' : '', abs = Math.abs(n);
        var symbol = (cur && (cur.symbol || cur.iso)) || '';
        var body;
        if (cur && usesIndian(cur.iso)) {
            if (abs >= 10000000) { body = (abs / 10000000).toFixed(2).replace(/\.?0+$/, '') + ' Cr'; }
            else if (abs >= 100000) { body = (abs / 100000).toFixed(2).replace(/\.?0+$/, '') + ' Lakh'; }
            else if (abs >= 1000) { body = (abs / 1000).toFixed(1).replace(/\.?0+$/, '') + 'K'; }
            else { body = abs.toLocaleString(window.navigator.language, { maximumFractionDigits: 2 }); }
        } else {
            if (abs >= 1000000000000) { body = (abs / 1000000000000).toFixed(1).replace(/\.?0+$/, '') + 'T'; }
            else if (abs >= 1000000000) { body = (abs / 1000000000).toFixed(1).replace(/\.?0+$/, '') + 'B'; }
            else if (abs >= 1000000) { body = (abs / 1000000).toFixed(1).replace(/\.?0+$/, '') + 'M'; }
            else if (abs >= 1000) { body = (abs / 1000).toFixed(1).replace(/\.?0+$/, '') + 'K'; }
            else { body = abs.toLocaleString(window.navigator.language, { maximumFractionDigits: 2 }); }
        }
        return sign + symbol + body;
    }
    function avatarColor(text) {
        var hash = 0, value = String(text || '');
        for (var i = 0; i < value.length; i++) { hash = (hash * 31 + value.charCodeAt(i)) % AVATAR_COLORS.length; }
        return AVATAR_COLORS[hash];
    }
    function initials(name) {
        return String(name || '').split(' ').slice(0, 2).map(function (w) { return w.charAt(0); }).join('').toUpperCase();
    }
    function icon(name) {
        if (name === 'chev') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>'; }
        if (name === 'chevL') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>'; }
        if (name === 'close') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>'; }
        return '';
    }

    var HELPERS = { formatMoney: formatMoney, formatCount: formatCount, escapeHtml: escapeHtml };

    VAS.KpiDrill = function (opts) {
        opts = opts || {};
        var pageSize = opts.pageSize || 7;
        var offset = 0, total = 0, seq = 0, currency = { symbol: '', iso: '', precision: 2 };
        var $modal, $body, $pager, $count;

        function build() {
            $modal = $(
                '<div class="MPC-kpidrill" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(opts.title || '') + '">' +
                    '<div class="MPC-kpidrill-scrim" data-drill-close></div>' +
                    '<section class="MPC-kpidrill-panel">' +
                        '<header class="MPC-kpidrill-head">' +
                            '<h2 class="MPC-kpidrill-title">' + escapeHtml(opts.title || '') + '</h2>' +
                            '<div class="MPC-kpidrill-head-r"><span class="MPC-kpidrill-count"></span>' +
                                '<button type="button" class="MPC-kpidrill-close" data-drill-close aria-label="' + escapeHtml(label('VAS_KpiDrill_Close', 'Close')) + '">' + icon('close') + '</button></div>' +
                        '</header>' +
                        '<div class="MPC-kpidrill-body"></div>' +
                        '<footer class="MPC-kpidrill-foot"><div class="MPC-kpidrill-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($modal);
            $body = $modal.find('.MPC-kpidrill-body');
            $pager = $modal.find('.MPC-kpidrill-pager');
            $count = $modal.find('.MPC-kpidrill-count');
            $modal.on('click', '[data-drill-close]', close);
            $modal.on('click', '.MPC-kpidrill-row', function () {
                var bp = Number($(this).attr('data-bp'));
                if (bp && typeof opts.navigate === 'function') { opts.navigate(bp); }
            });
            $modal.on('click', '.MPC-kpidrill-pgbtn', function () { turnPage($(this).attr('data-dir')); });
        }

        function load() {
            var mySeq = ++seq;
            $body.html('<div class="MPC-kpidrill-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $pager.empty();
            var data = { offset: offset, limit: pageSize };
            if (opts.extraParams) { for (var k in opts.extraParams) { if (opts.extraParams.hasOwnProperty(k)) { data[k] = opts.extraParams[k]; } } }
            $.ajax({
                url: VIS.Application.contextUrl + opts.endpoint,
                type: 'GET', cache: false, data: data,
                success: function (response) {
                    if (mySeq !== seq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed && parsed.error) { renderError(); return; }
                    var mapped = opts.mapData ? opts.mapData(parsed) : { items: parsed.items || [], total: Number(parsed.total || 0), currency: {} };
                    total = Number(mapped.total || 0);
                    currency = mapped.currency || currency;
                    $count.text(formatCount(total));
                    renderRows(mapped.items || []);
                },
                error: function () { if (mySeq === seq) { renderError(); } }
            });
        }

        function renderError() {
            $body.html('<div class="MPC-kpidrill-state">' + escapeHtml(label('VAS_KpiDrill_Error', 'Unable to load.')) + '</div>');
            $pager.empty();
        }

        function renderRows(items) {
            if (!items.length) {
                $body.html('<div class="MPC-kpidrill-state">' + escapeHtml(label('VAS_KpiDrill_Empty', 'Nothing here right now.')) + '</div>');
                $pager.empty();
                return;
            }
            var rows = items.map(function (item) {
                var r = opts.mapRow(item, currency, HELPERS) || {};
                var name = r.title || '';
                var toneCls = r.valueTone === 'danger' ? ' MPC-kpidrill-val-danger' : '';
                return '<button type="button" class="MPC-kpidrill-row" data-bp="' + Number(r.bpId) + '">' +
                    '<span class="MPC-kpidrill-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                    '<span class="MPC-kpidrill-main">' +
                        '<span class="MPC-kpidrill-rtitle" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                        (r.meta ? '<span class="MPC-kpidrill-rmeta" title="' + escapeHtml(r.meta) + '">' + escapeHtml(r.meta) + '</span>' : '') +
                    '</span>' +
                    (r.valueText ? '<span class="MPC-kpidrill-rval' + toneCls + '">' + escapeHtml(r.valueText) + '</span>' : '') +
                '</button>';
            }).join('');
            $body.html('<div class="MPC-kpidrill-list">' + rows + '</div>');

            var start = offset + 1, end = offset + items.length;
            var pages = Math.max(1, Math.ceil(total / pageSize));
            var current = Math.floor(offset / pageSize);
            var lbl = start + '–' + end + ' ' + label('VAS_KpiDrill_Of', 'of') + ' ' + formatCount(total);
            if (pages > 1) {
                $pager.html(
                    '<button type="button" class="MPC-kpidrill-pgbtn" data-dir="prev" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="MPC-kpidrill-pglabel">' + escapeHtml(lbl) + '</span>' +
                    '<button type="button" class="MPC-kpidrill-pgbtn" data-dir="next" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>'
                );
            } else {
                $pager.html('<span></span><span class="MPC-kpidrill-pglabel">' + escapeHtml(lbl) + '</span><span></span>');
            }
        }

        function turnPage(dir) {
            var next = offset + (dir === 'next' ? pageSize : -pageSize);
            if (next < 0) { next = 0; }
            if (next >= total) { return; }
            offset = next;
            load();
        }

        function open() {
            if (!$modal) { build(); }
            offset = 0;
            $modal.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-kpidrill-open');
            load();
        }
        function close() {
            if (!$modal) { return; }
            if (document.activeElement && $modal[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $modal.removeClass('is-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-kpidrill-open');
        }
        function isOpen() { return !!($modal && $modal.hasClass('is-open')); }
        function dispose() { if ($modal) { $modal.remove(); $modal = null; } }

        return { open: open, close: close, isOpen: isOpen, dispose: dispose };
    };

})(VAS, jQuery);
