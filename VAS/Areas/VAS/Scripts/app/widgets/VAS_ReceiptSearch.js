/**
 * VAS Receipt Search Widget
 * Purpose - Full-width (1x9) type-ahead search that finds AR receipts by
 *           customer, document number, amount, status, reconciled / allocated
 *           flag, bank, bank account, currency, or linked invoice document
 *           number. Selecting a row fires widgetFirevalueChanged with a
 *           TabWhereClause so the host window's payment tab opens that
 *           receipt (TabLayout 'Y' = single/form view), the same host
 *           mechanism the other dashboard widgets use.
 *
 * Data flow:
 *   - Client types -> Receipts/SearchReceipts (debounced, top 10) returns matching receipts.
 *   - Selecting a row -> widgetFirevalueChanged({ TabWhereClause, TabLayout, TabIndex }) -> host zoom.
 *
 * Structure mirrors VIS.SalesInvoiceCustomerSearchWidget.
 *
 * ── Labels / Message Keys (VAS_ prefix) ───────────────────────────────
 *  #  | Current Text                                  | Message Key
 * ----+-----------------------------------------------+----------------------------
 *  1  | Find receipts by customer, number, amount…    | VAS_ReceiptSearchPlaceholder
 *  2  | {n} matches / {n} match                       | VAS_Matches / VAS_Match
 *  3  | for                                           | VAS_For
 *  4  | No receipts match                             | VAS_NoReceiptsMatch
 *  5  | Searching…                                    | VAS_Searching
 *  6  | Clear                                         | VAS_Clear
 *  7  | Reconciled / Unreconciled                     | VAS_Reconciled / VAS_Unreconciled
 *  8  | Allocated / Unallocated                       | VAS_Allocated / VAS_Unallocated
 *  9  | Inv                                           | VAS_InvShort
 *  -  | Status labels (DR/IP/CO/CL/AP/NA/WP/WC/RE/VO/IN) | VAS_StatusDraft etc.
 * ──────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the font
       clamps resolve against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
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

    VAS.VAS_ReceiptSearch = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-rsw-root">');

        var $input;
        var $clear;
        /* Dropdown is mounted on <body> (fixed-positioned, anchored to the input)
           so the dashboard cell's overflow can never clip it. */
        var $suggest = null;
        var searchTimer = null;
        var reqSeq = 0;            /* guards against out-of-order responses */
        var cursor = -1;
        var matches = [];
        /* Scroll paging: the dropdown loads 25 rows per page and appends more as
           the user scrolls to the bottom. */
        var curQuery = '';         /* the active query the paging tracks */
        var curPage = 1;           /* last page loaded */
        var hasMore = false;       /* server signalled another page exists */
        var loadingMore = false;   /* a next-page fetch is in flight */

        var ZOOM_WINDOW_NAME_NEW = 'VAS_ARReceipt';
        var windowId = 0;

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
            DR: { label: lbl('VAS_StatusDraft', 'Draft'), bg: '#EDEDED', color: '#505050' },
            IP: { label: lbl('VAS_StatusInProgress', 'In Progress'), bg: '#FFF3CD', color: '#9A6500' },
            CO: { label: lbl('VAS_StatusCompleted', 'Completed'), bg: '#CCEFDD', color: '#0C5D38' },
            CL: { label: lbl('VAS_StatusClosed', 'Closed'), bg: '#DFF1FF', color: '#0E5DA8' },
            AP: { label: lbl('VAS_StatusApproved', 'Approved'), bg: '#CCEFDD', color: '#0C5D38' },
            NA: { label: lbl('VAS_StatusNotApproved', 'Not Approved'), bg: '#FFE8E8', color: '#C0392B' },
            WP: { label: lbl('VAS_StatusWaitingPayment', 'Waiting Payment'), bg: '#FFF3CD', color: '#9A6500' },
            WC: { label: lbl('VAS_StatusWaitingConfirm', 'Waiting Confirm'), bg: '#FFF3CD', color: '#9A6500' },
            RE: { label: lbl('VAS_StatusReversed', 'Reversed'), bg: '#FFE8E8', color: '#C0392B' },
            VO: { label: lbl('VAS_StatusVoided', 'Voided'), bg: '#FFE8E8', color: '#C0392B' },
            IN: { label: lbl('VAS_StatusInvalid', 'Invalid'), bg: '#FFE8E8', color: '#C0392B' }
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

        /* Amount markup: payment-currency symbol before the locale-formatted number. */
        function formatAmount(value, symbol) {
            var p = 2;
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    p = VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { p = 2; }
            var v = Number(value) || 0;
            var sign = v < 0 ? '-' : '';
            var num = Math.abs(v).toLocaleString(window.navigator.language, { minimumFractionDigits: p, maximumFractionDigits: p });
            var sym = symbol ? '<span class="vas-rsw-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + num;
        }

        /* Render the bank info as "Bank · ****1234" when both parts are known. */
        function formatBank(bankName, accountNo) {
            var bn = (bankName || '').trim();
            var an = (accountNo || '').trim();
            var last4 = an.length > 4 ? an.slice(-4) : an;
            if (bn && last4) { return bn + ' · ****' + last4; }
            if (bn) { return bn; }
            if (last4) { return '****' + last4; }
            return '';
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
        /* Saturated status colour for the meta dot. */
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
            var $zone = $('<div class="vas-rsw-zone">');

            var searchSvg =
                '<svg class="vas-rsw-search-icon" width="19" height="19" viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>';
            var clearSvg =
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>';

            var $pill = $('<div class="vas-rsw-input">');
            $pill.append(searchSvg);

            $input = $(
                '<input type="text" class="vas-rsw-field" autocomplete="off" ' +
                'role="combobox" aria-autocomplete="list" aria-expanded="false" ' +
                'placeholder="' + escapeHtml(lbl('VAS_ReceiptSearchPlaceholder', 'Find receipts by customer, number, amount, bank, status…')) + '">'
            );

            $clear = $('<button type="button" class="vas-rsw-clear" aria-label="' + escapeHtml(lbl('VAS_Clear', 'Clear')) + '">' + clearSvg + '</button>');

            $pill.append($input).append($clear);
            $zone.append($pill);

            /* Gutter band — the pill floats centered with equal empty space left & right
               (the search pill IS the widget, no card wrapper), per design.md
               "Full-Width Dashboard Search Widget". */
            var $esw = $('<div class="vas-rsw-esw">');
            $esw.append($zone);
            $root.append($esw);
        }

        /* ── Suggest dropdown (mounted on <body>) ── */
        function ensureSuggest() {
            if ($suggest) return;
            $suggest = $('<div class="vas-rsw-suggest" role="listbox">');
            $('body').append($suggest);
            $suggest.on('click', '.vas-rsw-line', function () {
                var idx = parseInt($(this).attr('data-index'), 10);
                if (matches[idx]) zoomReceipt(matches[idx].cPaymentId);
            });

            /* Infinite scroll: once near the bottom, pull the next 25-row page. */
            $suggest.on('scroll', function () {
                if (!hasMore || loadingMore) { return; }
                var el = this;
                if (el.scrollTop + el.clientHeight >= el.scrollHeight - 40) {
                    fetchPage(curPage + 1, true);
                }
            });
        }

        /* Anchor the dropdown to the input pill (fixed positioning). */
        function positionSuggest() {
            if (!$suggest) return;
            var el = $input.closest('.vas-rsw-input')[0];
            if (!el) return;
            var r = el.getBoundingClientRect();
            $suggest.css({ left: r.left + 'px', top: (r.bottom + 8) + 'px', width: r.width + 'px' });
        }

        function openSuggest() {
            ensureSuggest();
            positionSuggest();
            $suggest.addClass('is-open');
            $input.attr('aria-expanded', 'true');
            $(window).on('scroll.vasRsw resize.vasRsw', positionSuggest);
        }

        function closeSuggest() {
            if ($suggest) $suggest.removeClass('is-open');
            $input.attr('aria-expanded', 'false');
            cursor = -1;
            $(window).off('scroll.vasRsw resize.vasRsw');
        }

        /* ── Events ── */
        function bindEvents() {
            $root.on('input', '.vas-rsw-field', function () {
                $clear.toggleClass('is-visible', $input.val().length > 0);
                if (searchTimer) clearTimeout(searchTimer);
                var q = $input.val().trim();
                if (!q) { closeSuggest(); return; }
                searchTimer = setTimeout(runSearch, 250);
            });

            $root.on('focus', '.vas-rsw-field', function () {
                if ($input.val().trim()) runSearch();
            });

            $root.on('click', '.vas-rsw-clear', function () {
                $input.val('');
                $clear.removeClass('is-visible');
                closeSuggest();
                $input.focus();
            });

            $root.on('keydown', '.vas-rsw-field', function (e) {
                var lines = $suggest ? $suggest.find('.vas-rsw-line') : $();
                if (!$suggest || !$suggest.hasClass('is-open') || lines.length === 0) {
                    if (e.key === 'Escape') closeSuggest();
                    return;
                }
                if (e.key === 'ArrowDown') { e.preventDefault(); cursor = Math.min(cursor + 1, lines.length - 1); paintCursor(lines); }
                else if (e.key === 'ArrowUp') { e.preventDefault(); cursor = Math.max(cursor - 1, 0); paintCursor(lines); }
                else if (e.key === 'Enter') { if (cursor >= 0 && matches[cursor]) { e.preventDefault(); zoomReceipt(matches[cursor].cPaymentId); } }
                else if (e.key === 'Escape') { closeSuggest(); }
            });

            /* Click outside the input zone or the dropdown closes it. */
            $(document).on('mousedown.vasRsw-' + ($self.AD_UserHomeWidgetID || ''), function (e) {
                if ($(e.target).closest('.vas-rsw-zone').length) return;
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

        /* ── Run a fresh search (resets to page 1) and render the dropdown ── */
        function runSearch() {
            var q = $input.val().trim();
            if (!q) { closeSuggest(); return; }

            ensureSuggest();
            $suggest.html('<div class="vas-rsw-state">' + escapeHtml(lbl('VAS_Searching', 'Searching…')) + '</div>');
            openSuggest();

            curQuery = q;
            curPage = 1;
            hasMore = false;
            loadingMore = false;
            matches = [];
            fetchPage(1, false);
        }

        /* Fetch one 25-row page. append=false replaces the list (fresh search);
           append=true adds the next page's rows to the bottom (infinite scroll). */
        function fetchPage(page, append) {
            var q = curQuery;
            if (!q) { return; }
            if (append) { loadingMore = true; showLoadMore(true); }

            var mySeq = ++reqSeq;
            $.ajax({
                url: VIS.Application.contextUrl + 'Receipts/SearchReceipts',
                type: 'GET',
                data: { q: q, max: 25, page: page },
                success: function (res) {
                    /* Ignore stale responses (a newer keystroke / page already fired). */
                    if (mySeq !== reqSeq) return;
                    var data = parseResponse(res);
                    var rows = (data && data.rows) ? data.rows : [];
                    hasMore = !!(data && data.hasMore);
                    curPage = (data && data.page) ? data.page : page;
                    if (append) { appendSuggest(rows, q); loadingMore = false; }
                    else { renderSuggest(rows, q); }
                },
                error: function () {
                    if (mySeq !== reqSeq) return;
                    hasMore = false;
                    if (append) { loadingMore = false; showLoadMore(false); }
                    else { renderSuggest([], q); }
                }
            });
        }

        /* Header count line. Shows "N+" while more pages remain — the exact total is
           not counted (the server signals hasMore via a single look-ahead row). */
        function metaHtml(q) {
            var word = matches.length === 1 ? lbl('VAS_Match', 'match') : lbl('VAS_Matches', 'matches');
            var count = matches.length + (hasMore ? '+' : '');
            return '<div class="vas-rsw-meta"><strong>' + count + '</strong> ' + word + ' ' + lbl('VAS_For', 'for') + ' "' + escapeHtml(q) + '"</div>';
        }

        function updateMeta(q) {
            if (!$suggest) return;
            var $meta = $suggest.find('.vas-rsw-meta');
            if ($meta.length) { $meta.replaceWith(metaHtml(q)); }
        }

        /* Build row markup for a slice, using absolute indices (startIndex + j) so
           data-index maps into `matches` for click / keyboard selection.
           Per-row meta order: status-dot + docNo · bank · currency · date · amount ·
           status · invoice ref · [allocated / reconciled badges] — data first, tags last. */
        function rowsHtml(rows, startIndex, q) {
            var html = '';
            $.each(rows, function (j, r) {
                var i = startIndex + j;
                var color = PALETTE[i % PALETTE.length];

                var pieces = [];
                pieces.push('<span class="vas-rsw-dot" style="background:' + statusColor(r.docStatus) + ';"></span>' + highlight(r.documentNo || '', q));

                var bankText = formatBank(r.bankName, r.accountNo);
                if (bankText) {
                    pieces.push(highlight(bankText, q));
                }

                if (r.currencyIso) {
                    pieces.push(escapeHtml(r.currencyIso));
                }

                if (r.dateAcct) {
                    pieces.push(escapeHtml(r.dateAcct));
                }

                pieces.push(formatAmount(r.payAmount, r.curSymbol));

                pieces.push(escapeHtml(statusLabel(r.docStatus)));

                if (r.invoiceDocumentNo) {
                    pieces.push(escapeHtml(lbl('VAS_InvShort', 'Inv')) + ' ' + highlight(r.invoiceDocumentNo, q));
                }

                if (r.isAllocated) {
                    pieces.push('<span class="vas-rsw-flag is-on">' + escapeHtml(lbl('VAS_Allocated', 'Allocated')) + '</span>');
                }
                if (r.isReconciled) {
                    pieces.push('<span class="vas-rsw-flag is-on">' + escapeHtml(lbl('VAS_Reconciled', 'Reconciled')) + '</span>');
                }

                var meta = pieces.join(' · ');

                html += '<div class="vas-rsw-line" data-index="' + i + '" role="option">' +
                    '<div class="vas-rsw-avatar" style="background:' + color + ';">' + escapeHtml(initials(r.customerName)) + '</div>' +
                    '<div class="vas-rsw-info">' +
                    '<div class="vas-rsw-name">' + highlight(r.customerName || '—', q) + '</div>' +
                    '<div class="vas-rsw-rowmeta">' + meta + '</div>' +
                    '</div>' +
                    '</div>';
            });
            return html;
        }

        /* Fresh render (page 1): replace the whole dropdown body. */
        function renderSuggest(rows, q) {
            matches = rows || [];
            cursor = -1;
            ensureSuggest();

            if (matches.length === 0) {
                hasMore = false;
                $suggest.html('<div class="vas-rsw-empty">' + lbl('VAS_NoReceiptsMatch', 'No receipts match') + ' "<strong>' + escapeHtml(q) + '</strong>".</div>');
                openSuggest();
                return;
            }

            $suggest.html(metaHtml(q) + '<div class="vas-rsw-list">' + rowsHtml(matches, 0, q) + '</div>');
            $suggest.scrollTop(0);
            openSuggest();
        }

        /* Append the next page's rows to the bottom (infinite scroll). */
        function appendSuggest(rows, q) {
            ensureSuggest();
            showLoadMore(false);
            if (!rows || rows.length === 0) { updateMeta(q); return; }

            var startIndex = matches.length;
            matches = matches.concat(rows);

            var $list = $suggest.find('.vas-rsw-list');
            if ($list.length === 0) { renderSuggest(matches, q); return; }

            $list.append(rowsHtml(rows, startIndex, q));
            updateMeta(q);
        }

        /* Show / hide the bottom "Loading more…" indicator during a page fetch. */
        function showLoadMore(show) {
            if (!$suggest) return;
            var $more = $suggest.find('.vas-rsw-more');
            if (show) {
                if ($more.length === 0) {
                    $suggest.append('<div class="vas-rsw-more">' + escapeHtml(lbl('VAS_LoadingMore', 'Loading more…')) + '</div>');
                }
            } else {
                $more.remove();
            }
        }

        /* ── Zoom: fire the value the host listens for to open this receipt in the
           window's first tab, filtered to the single record (single/form layout). ── */
        function zoomReceipt(cPaymentId) {
            if (!cPaymentId) return;
            closeSuggest();
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Payment.C_Payment_ID=" + cPaymentId,
                    "TabLayout": "Y",   /* 'N' Grid, 'Y' Single, 'C' Card */
                    "TabIndex": "0"
                });
            }
            else {
                VAS.ZoomUtil.zoomToRecord("C_Payment_ID", cPaymentId, windowId, ZOOM_WINDOW_NAME_NEW, "")
                    .done(function (id) {
                        if (id > 0) { windowId = id; }
                    });
            }
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
            $(document).off('mousedown.vasRsw-' + ($self.AD_UserHomeWidgetID || ''));
            $(window).off('scroll.vasRsw resize.vasRsw');
            if (searchTimer) clearTimeout(searchTimer);
            if ($suggest) { $suggest.remove(); $suggest = null; }
            $root.remove();
        };
    };

    VAS.VAS_ReceiptSearch.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Relay the fired value (zoom params) to the registered widget host. */
    VAS.VAS_ReceiptSearch.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    /* The widget host registers itself here so the widget can drive the host (zoom). */
    VAS.VAS_ReceiptSearch.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_ReceiptSearch.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the font clamps read. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_ReceiptSearch.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_ReceiptSearch.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VAS, jQuery);
