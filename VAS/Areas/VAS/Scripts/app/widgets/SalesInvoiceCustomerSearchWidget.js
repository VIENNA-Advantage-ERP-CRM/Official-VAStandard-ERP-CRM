/**
 * Sales Invoice Customer Search Widget
 * Purpose - Full-width (1x9) type-ahead search that finds sales invoices by customer, document
 *           number, amount or status and zooms the chosen record. Selecting a suggestion fires
 *           widgetFirevalueChanged with a TabWhereClause so the host window's invoice tab opens that
 *           record (TabLayout 'Y' = single/form view), the same host mechanism the other dashboard
 *           widgets use.
 *
 * Data flow:
 *   - Client types -> Invoices/SearchInvoices (debounced, top 10) returns matching invoices.
 *   - Selecting a row -> widgetFirevalueChanged({ TabWhereClause, TabLayout, TabIndex }) -> host zoom.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                  | Message Key            | MsgText
 * ----+-----------------------------------------------+------------------------+----------------------------
 *  1  | Find invoices by customer, number, amount…    | VIS_InvoiceSearchPlaceholder | Find invoices by customer, number, amount or status…
 *  2  | {n} matches / {n} match                       | VIS_Matches / VIS_Match | matches / match
 *  3  | for                                           | VIS_For                | for
 *  4  | No invoices match                             | VIS_NoInvoicesMatch    | No invoices match
 *  5  | Searching…                                    | VIS_Searching          | Searching…
 *  6  | Clear                                         | VIS_Clear              | Clear
 *  -  | Status labels (DR/IP/CO/CL/AP/NA/WP/WC/RE/VO/IN) | VIS_StatusDraft etc. | Draft / In Progress / ...
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.SalesInvoiceCustomerSearchWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-sics-root">');

        var $input;
        var $clear;
        /* The dropdown is mounted on <body> (fixed-positioned, anchored to the input) so the dashboard
           cell's overflow can never clip it. */
        var $suggest = null;
        var searchTimer = null;
        var reqSeq = 0;            /* guards against out-of-order responses */
        var cursor = -1;
        var matches = [];

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(s) {
            if (s == null) return '';
            return String(s).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        /* Document-status chip colours (codes, not the attention buckets). */
        var STATUS = {
            DR: { label: lbl('VIS_StatusDraft', 'Draft'), bg: '#EDEDED', color: '#505050' },
            IP: { label: lbl('VIS_StatusInProgress', 'In Progress'), bg: '#FFF3CD', color: '#9A6500' },
            CO: { label: lbl('VIS_StatusCompleted', 'Completed'), bg: '#CCEFDD', color: '#0C5D38' },
            CL: { label: lbl('VIS_StatusClosed', 'Closed'), bg: '#DFF1FF', color: '#0E5DA8' },
            AP: { label: lbl('VIS_StatusApproved', 'Approved'), bg: '#CCEFDD', color: '#0C5D38' },
            NA: { label: lbl('VIS_StatusNotApproved', 'Not Approved'), bg: '#FFE8E8', color: '#C0392B' },
            WP: { label: lbl('VIS_StatusWaitingPayment', 'Waiting Payment'), bg: '#FFF3CD', color: '#9A6500' },
            WC: { label: lbl('VIS_StatusWaitingConfirm', 'Waiting Confirm'), bg: '#FFF3CD', color: '#9A6500' },
            RE: { label: lbl('VIS_StatusReversed', 'Reversed'), bg: '#FFE8E8', color: '#C0392B' },
            VO: { label: lbl('VIS_StatusVoided', 'Voided'), bg: '#FFE8E8', color: '#C0392B' },
            IN: { label: lbl('VIS_StatusInvalid', 'Invalid'), bg: '#FFE8E8', color: '#C0392B' }
        };

        /* Avatar palette (cycles per row), matching the search-widget design. */
        var PALETTE = ['#0083DA', '#019D89', '#D78B10', '#5F4AA6', '#A33F3F', '#2084C4'];

        /* Two-letter initials from the customer name. */
        function initials(name) {
            if (!name) return '#';
            var parts = name.trim().split(/\s+/);
            if (parts.length >= 2) return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
            return name.substring(0, 2).toUpperCase();
        }

        /* Amount markup: invoice-currency symbol before the locale-formatted number. */
        function formatAmount(value, symbol) {
            var p = VIS.Env.getCtx().getStdPrecision();
            var v = Number(value) || 0;
            var sign = v < 0 ? '-' : '';
            var num = Math.abs(v).toLocaleString(window.navigator.language, { minimumFractionDigits: p, maximumFractionDigits: p });
            var sym = symbol ? '<span class="vas-sics-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + num;
        }

        /* Wrap the first case-insensitive match of q in <mark>. */
        function highlight(text, q) {
            text = text == null ? '' : String(text);
            if (!q) return escapeHtml(text);
            var idx = text.toLowerCase().indexOf(q.toLowerCase());
            if (idx === -1) return escapeHtml(text);
            return escapeHtml(text.slice(0, idx)) +
                '<mark>' + escapeHtml(text.slice(idx, idx + q.length)) + '</mark>' +
                escapeHtml(text.slice(idx + q.length));
        }

        function statusLabel(code) {
            var cfg = STATUS[code];
            return cfg ? cfg.label : (code || '—');
        }
        /* Saturated status colour for the meta dot (the chip backgrounds are too pale for a dot). */
        function statusColor(code) {
            var cfg = STATUS[code];
            return cfg ? cfg.color : '#748494';
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            bindEvents();
        };

        /* ── Build DOM ── */
        function createWidget() {
            var $zone = $('<div class="vas-sics-zone">');

            var searchSvg =
                '<svg class="vas-sics-search-icon" width="19" height="19" viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>';
            var clearSvg =
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>';

            var $pill = $('<div class="vas-sics-input">');
            $pill.append(searchSvg);

            $input = $(
                '<input type="text" class="vas-sics-field" autocomplete="off" ' +
                'role="combobox" aria-autocomplete="list" aria-expanded="false" ' +
                'placeholder="' + escapeHtml(lbl('VIS_InvoiceSearchPlaceholder', 'Find invoices by customer, number, amount or status…')) + '">'
            );

            $clear = $('<button type="button" class="vas-sics-clear" aria-label="' + escapeHtml(lbl('VIS_Clear', 'Clear')) + '">' + clearSvg + '</button>');

            $pill.append($input).append($clear);
            $zone.append($pill);

            /* Gutter band: the pill floats centered with equal empty space left & right (the search
               pill IS the widget — no card wrapper), per the attached design and design.md
               "Full-Width Dashboard Search Widget". */
            var $esw = $('<div class="vas-sics-esw">');
            $esw.append($zone);
            $root.append($esw);
        }

        /* ── Suggest dropdown (mounted on <body>) ── */
        function ensureSuggest() {
            if ($suggest) return;
            $suggest = $('<div class="vas-sics-suggest" role="listbox">');
            $('body').append($suggest);
            /* Clicks inside the dropdown act on its rows; outside clicks close it. */
            $suggest.on('click', '.vas-sics-line', function () {
                var idx = parseInt($(this).attr('data-index'), 10);
                if (matches[idx]) zoomInvoice(matches[idx].cInvoiceId);
            });
        }

        /* Anchor the dropdown to the input pill (fixed positioning). */
        function positionSuggest() {
            if (!$suggest) return;
            var el = $input.closest('.vas-sics-input')[0];
            if (!el) return;
            var r = el.getBoundingClientRect();
            $suggest.css({ left: r.left + 'px', top: (r.bottom + 8) + 'px', width: r.width + 'px' });
        }

        function openSuggest() {
            ensureSuggest();
            positionSuggest();
            $suggest.addClass('is-open');
            $input.attr('aria-expanded', 'true');
            $(window).on('scroll.vasSics resize.vasSics', positionSuggest);
        }

        function closeSuggest() {
            if ($suggest) $suggest.removeClass('is-open');
            $input.attr('aria-expanded', 'false');
            cursor = -1;
            $(window).off('scroll.vasSics resize.vasSics');
        }

        /* ── Events ── */
        function bindEvents() {
            $root.on('input', '.vas-sics-field', function () {
                $clear.toggleClass('is-visible', $input.val().length > 0);
                if (searchTimer) clearTimeout(searchTimer);
                var q = $input.val().trim();
                if (!q) { closeSuggest(); return; }
                searchTimer = setTimeout(runSearch, 250);
            });

            $root.on('focus', '.vas-sics-field', function () {
                if ($input.val().trim()) runSearch();
            });

            $root.on('click', '.vas-sics-clear', function () {
                $input.val('');
                $clear.removeClass('is-visible');
                closeSuggest();
                $input.focus();
            });

            $root.on('keydown', '.vas-sics-field', function (e) {
                var lines = $suggest ? $suggest.find('.vas-sics-line') : $();
                if (!$suggest || !$suggest.hasClass('is-open') || lines.length === 0) {
                    if (e.key === 'Escape') closeSuggest();
                    return;
                }
                if (e.key === 'ArrowDown') { e.preventDefault(); cursor = Math.min(cursor + 1, lines.length - 1); paintCursor(lines); }
                else if (e.key === 'ArrowUp') { e.preventDefault(); cursor = Math.max(cursor - 1, 0); paintCursor(lines); }
                else if (e.key === 'Enter') { if (cursor >= 0 && matches[cursor]) { e.preventDefault(); zoomInvoice(matches[cursor].cInvoiceId); } }
                else if (e.key === 'Escape') { closeSuggest(); }
            });

            /* Click anywhere outside the input zone or the dropdown closes it. */
            $(document).on('mousedown.vasSics-' + ($self.AD_UserHomeWidgetID || ''), function (e) {
                if ($(e.target).closest('.vas-sics-zone').length) return;
                if ($suggest && $(e.target).closest($suggest).length) return;
                closeSuggest();
            });
        }

        function paintCursor(lines) {
            lines.each(function (i) { $(this).toggleClass('is-cursor', i === cursor); });
            if (cursor >= 0 && lines[cursor]) lines[cursor].scrollIntoView({ block: 'nearest' });
        }

        /* Defensively unwrap the controller payload (may arrive double-encoded). */
        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        /* ── Run the search and render the dropdown ── */
        function runSearch() {
            var q = $input.val().trim();
            if (!q) { closeSuggest(); return; }

            ensureSuggest();
            $suggest.html('<div class="vas-sics-state">' + escapeHtml(lbl('VIS_Searching', 'Searching…')) + '</div>');
            openSuggest();

            var mySeq = ++reqSeq;
            $.ajax({
                url: VIS.Application.contextUrl + 'Invoices/SearchInvoices',
                type: 'GET',
                data: { q: q, max: 10 },
                success: function (res) {
                    /* Ignore stale responses (a newer keystroke already fired). */
                    if (mySeq !== reqSeq) return;
                    var data = parseResponse(res);
                    var rows = (data && data.rows) ? data.rows : [];
                    renderSuggest(rows, q);
                },
                error: function () {
                    if (mySeq !== reqSeq) return;
                    renderSuggest([], q);
                }
            });
        }

        function renderSuggest(rows, q) {
            matches = rows || [];
            cursor = -1;
            ensureSuggest();

            if (matches.length === 0) {
                $suggest.html('<div class="vas-sics-empty">' + lbl('VIS_NoInvoicesMatch', 'No invoices match') + ' "<strong>' + escapeHtml(q) + '</strong>".</div>');
                openSuggest();
                return;
            }

            var word = matches.length === 1 ? lbl('VIS_Match', 'match') : lbl('VIS_Matches', 'matches');
            var html = '<div class="vas-sics-meta"><strong>' + matches.length + '</strong> ' + word + ' ' + lbl('VIS_For', 'for') + ' "' + escapeHtml(q) + '"</div>';

            $.each(matches, function (i, r) {
                var color = PALETTE[i % PALETTE.length];
                /* Meta: status-coloured dot + "docNo · amount · status · date" (highlighted matches),
                   mirroring the design's avatar + name + dotted-meta line. */
                var meta = '<span class="vas-sics-dot" style="background:' + statusColor(r.docStatus) + ';"></span>' +
                    highlight(r.documentNo, q) +
                    ' · ' + formatAmount(r.grandTotal, r.curSymbol) +
                    ' · ' + escapeHtml(statusLabel(r.docStatus)) +
                    (r.dateInvoiced ? ' · ' + escapeHtml(r.dateInvoiced) : '');
                html += '<div class="vas-sics-line" data-index="' + i + '" role="option">' +
                    '<div class="vas-sics-avatar" style="background:' + color + ';">' + escapeHtml(initials(r.customerName)) + '</div>' +
                    '<div class="vas-sics-info">' +
                    '<div class="vas-sics-name">' + highlight(r.customerName || '—', q) + '</div>' +
                    '<div class="vas-sics-rowmeta">' + meta + '</div>' +
                    '</div>' +
                    '</div>';
            });

            $suggest.html(html);
            openSuggest();
        }

        /* ── Zoom: fire the value the host listens for to open this invoice in the window's first tab,
           filtered to the single record (single/form layout). ── */
        function zoomInvoice(cInvoiceId) {
            if (!cInvoiceId) return;
            $self.widgetFirevalueChanged({
                "TabWhereClause": "C_Invoice.C_Invoice_ID=" + cInvoiceId,
                "TabLayout": "Y",   /* 'N' Grid, 'Y' Single, 'C' Card */
                "TabIndex": "0"
            });
            closeSuggest();
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            closeSuggest();
            if ($input) { $input.val(''); }
            if ($clear) { $clear.removeClass('is-visible'); }
            matches = [];
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $(document).off('mousedown.vasSics-' + ($self.AD_UserHomeWidgetID || ''));
            $(window).off('scroll.vasSics resize.vasSics');
            if (searchTimer) clearTimeout(searchTimer);
            if ($suggest) { $suggest.remove(); $suggest = null; }
            $root.remove();
        };
    };

    VIS.SalesInvoiceCustomerSearchWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Relay the fired value (zoom params) to the registered widget host. */
    VIS.SalesInvoiceCustomerSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    /* The widget host registers itself here so the widget can drive the host (zoom). */
    VIS.SalesInvoiceCustomerSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VIS.SalesInvoiceCustomerSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.SalesInvoiceCustomerSearchWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.SalesInvoiceCustomerSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
