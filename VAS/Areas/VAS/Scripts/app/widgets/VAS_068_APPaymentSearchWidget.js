/************************************************************
 * Module Name    : VAS
 * Purpose        : AP Payment Search Widget
 *                  Full-width 9x1 dashboard search bar that searches
 *                  outgoing payments (C_Payment, IsReceipt = 'N') and
 *                  shows the most relevant matches in a dropdown.
 *                  Clicking a result zooms to the payment record.
 * chronological  : Development
 * Created Date   : 13 June 2026
 * Created by     : Claude (VAS widget pattern)
 *
 * AD_Message keys used (add via System Messages):
 *   VAS_068_Placeholder       => "Search AP payments by no., vendor, cheque/trx no., bank account..."
 *   VAS_DocSearch_TypeToSearch=> "Type at least 2 characters to search"
 *   VAS_DocSearch_NoResults   => "No matching documents"
 *   VAS_DocSearch_Error       => "Search failed. Please try again."
 *   VAS_DocSearch_Results     => "results"
 *   VAS_DocSearch_Invoice     => "Invoice"
 *   VAS_Allocated             => "Allocated"
 *   VAS_Reconciled            => "Reconciled"
 *
 * Result row: initials avatar, vendor name as the headline, then one
 * dot-separated meta line - status dot + doc no, document type, bank
 * account, currency, matched invoice, status, allocated / reconciled
 * flags. Amount and date sit in the right-hand column so figures stay
 * aligned down the list; the amount is in the payment's OWN currency,
 * its symbol flush against the number.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_068_APPaymentSearchWidget = function () {
        // ---- Per-widget configuration ----
        var ENDPOINT      = 'VAS/VAS_068_APPaymentSearchWidget/Search';
        var ZOOM_TABLE    = 'C_Payment';
        // Zoom target when the widget is NOT hosted inside a window (windowNo < 0).
        // The search payload already carries the resolved AD_Window_ID; these names
        // are the fallback VAS.ZoomUtil resolves from if it ever comes back 0.
        var ZOOM_WINDOW_NAME_NEW = 'VAS_APPayment';
        var ZOOM_WINDOW_NAME_OLD = 'Payment';
        var PLACEHOLDER_K = 'VAS_068_Placeholder';
        var PLACEHOLDER_D = 'Search AP payments by no., vendor, cheque/trx no., bank account...';

        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-dssrch-root">');
        var $bar, $input, $panel;
        var widgetID = null;

        var windowId = 0;
        var curSymbol = '';
        var stdPrecision = 2;
        var debounceTimer = null;
        var requestSeq = 0;
        var MIN_LEN = 2;
        var PAGE_SIZE = 25;
        var currentTerm = '';
        var loadedCount = 0;
        var hasMore = false;
        var isLoadingMore = false;

        /* ---- Initialise ---- */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'hidden';
        };

        /* ---- No initial data to load; framework hook just clears busy ---- */
        this.intialLoad = function () {
            $bsyDiv[0].style.visibility = 'hidden';
        };

        function buildShell() {
            var placeholder = msg(PLACEHOLDER_K, PLACEHOLDER_D);

            $bar = $('<div class="vas-dssrch-bar" id="vas_dssrch_bar_' + widgetID + '">');
            $bar.append(
                '<svg class="vas-dssrch-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                    '<circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line>' +
                '</svg>' +
                '<span class="vas-dssrch-spin"></span>'
            );
            $input = $('<input type="text" class="vas-dssrch-input" autocomplete="off" spellcheck="false">')
                .attr('placeholder', placeholder);
            $bar.append($input);

            var $clear = $('<button type="button" class="vas-dssrch-clear" tabindex="-1">&#215;</button>');
            $bar.append($clear);
            $root.append($bar);

            // Dropdown lives on <body> so the dashboard cell's overflow:hidden
            // cannot clip it; positioned as a fixed popover under the bar.
            $panel = $('<div class="vas-dssrch-panel" id="vas_dssrch_panel_' + widgetID + '">');
            $('body').append($panel);
            $panel.on('scroll', onPanelScroll);

            wireEvents($clear);
        }

        function wireEvents($clear) {
            $input.on('input', function () {
                var term = $.trim($input.val());
                $bar.toggleClass('vas-dssrch-has-text', term.length > 0);
                scheduleSearch(term);
            });
            $input.on('focus', function () {
                var term = $.trim($input.val());
                if (term.length >= MIN_LEN && $panel.children().length > 0) { openPanel(); }
            });
            $input.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closePanel(); }
            });
            $clear.on('click', function () {
                $input.val('');
                $bar.removeClass('vas-dssrch-has-text');
                setBusy(false);
                closePanel();
                $input.focus();
            });

            $self._onDocClick = function (e) {
                if (!$panel.hasClass('vas-dssrch-open')) { return; }
                if ($bar[0].contains(e.target) || $panel[0].contains(e.target)) { return; }
                closePanel();
            };
            $self._onReflow = function () {
                if ($panel.hasClass('vas-dssrch-open')) { positionPanel(); }
            };
            document.addEventListener('mousedown', $self._onDocClick, true);
            window.addEventListener('resize', $self._onReflow, true);
            window.addEventListener('scroll', $self._onReflow, true);
        }

        /* ---- Debounced search ---- */
        function scheduleSearch(term) {
            if (debounceTimer) { window.clearTimeout(debounceTimer); }
            if (term.length === 0) { setBusy(false); closePanel(); return; }
            if (term.length < MIN_LEN) { setBusy(false); renderHint(); return; }
            setBusy(true);
            debounceTimer = window.setTimeout(function () { runSearch(term); }, 280);
        }

        function runSearch(term) {
            var mySeq = ++requestSeq;
            currentTerm = term;
            loadedCount = 0;
            hasMore = false;
            isLoadingMore = false;
            $.ajax({
                url: VIS.Application.contextUrl + ENDPOINT,
                data: { query: term, maxRows: PAGE_SIZE, offset: 0 },
                dataType: 'json',
                async: true,
                success: function (res) {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);
                    var data = null;
                    try { data = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    if (data) {
                        curSymbol = data.CurSymbol || '';
                        stdPrecision = (typeof data.StdPrecision === 'number') ? data.StdPrecision : 2;
                        windowId = VIS.Utility.Util.getValueOfInt(data.WindowId);
                    }
                    var items = data ? (data.Items || []) : [];
                    loadedCount = items.length;
                    hasMore = items.length === PAGE_SIZE;
                    renderResults(items);
                },
                error: function () {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);
                    renderError();
                }
            });
        }

        /* ---- Infinite scroll: fetch the next page and append ---- */
        function onPanelScroll() {
            if (!hasMore || isLoadingMore) { return; }
            if (!$panel.hasClass('vas-dssrch-open')) { return; }
            var el = $panel[0];
            if (el.scrollTop + el.clientHeight >= el.scrollHeight - 56) { loadMore(); }
        }

        function loadMore() {
            if (isLoadingMore || !hasMore) { return; }
            isLoadingMore = true;
            var mySeq = requestSeq;
            var term = currentTerm;
            var offset = loadedCount;
            showMoreSpinner(true);
            $.ajax({
                url: VIS.Application.contextUrl + ENDPOINT,
                data: { query: term, maxRows: PAGE_SIZE, offset: offset },
                dataType: 'json',
                async: true,
                success: function (res) {
                    if (mySeq !== requestSeq) { isLoadingMore = false; return; }
                    var data = null;
                    try { data = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    var items = data ? (data.Items || []) : [];
                    appendResults(items);
                    loadedCount += items.length;
                    hasMore = items.length === PAGE_SIZE;
                    isLoadingMore = false;
                    showMoreSpinner(false);
                    updateCount();
                },
                error: function () {
                    if (mySeq !== requestSeq) { isLoadingMore = false; return; }
                    isLoadingMore = false;
                    showMoreSpinner(false);
                }
            });
        }

        /* ---- Rendering ---- */
        function renderHint() {
            $panel.html(stateHtml(searchSvg(), msg('VAS_DocSearch_TypeToSearch', 'Type at least 2 characters to search'), false));
            openPanel();
        }

        function renderError() {
            $panel.html(stateHtml(alertSvg(), msg('VAS_DocSearch_Error', 'Search failed. Please try again.'), true));
            openPanel();
        }

        function renderResults(items) {
            if (!items || items.length === 0) {
                $panel.html(stateHtml(searchSvg(), msg('VAS_DocSearch_NoResults', 'No matching documents'), false));
                openPanel();
                return;
            }

            var html = '<div class="vas-dssrch-count"></div><div class="vas-dssrch-list">';
            for (var i = 0; i < items.length; i++) {
                html += buildRow(items[i], i, currentTerm);
            }
            html += '</div><div class="vas-dssrch-more"><span class="vas-dssrch-more-spin"></span></div>';
            $panel.html(html);

            bindRowClicks($panel.find('.vas-dssrch-list .vas-dssrch-row'));
            updateCount();
            $panel.scrollTop(0);
            openPanel();
        }

        function appendResults(items) {
            if (!items || items.length === 0) { return; }
            /* loadedCount is still the pre-append total here, so it is the absolute
               index of the first new row - which keeps the avatar palette cycling
               continuously instead of restarting at every page. */
            var startIndex = loadedCount;
            var html = '';
            for (var i = 0; i < items.length; i++) {
                html += buildRow(items[i], startIndex + i, currentTerm);
            }
            var $rows = $(html).filter('.vas-dssrch-row');
            $panel.find('.vas-dssrch-list').append($rows);
            bindRowClicks($rows);
        }

        function bindRowClicks($rows) {
            $rows.on('click', function () {
                if ($(this).hasClass('vas-dssrch-nozoom')) { return; }
                zoomTo(VIS.Utility.Util.getValueOfInt($(this).attr('data-id')));
            });
        }

        function updateCount() {
            var text = loadedCount + (hasMore ? '+' : '') + ' ' + msg('VAS_DocSearch_Results', 'results');
            $panel.find('.vas-dssrch-count').text(text);
        }

        function showMoreSpinner(on) {
            $panel.find('.vas-dssrch-more').toggleClass('vas-dssrch-more-active', !!on);
        }

        /* Vendor-first row: initials avatar, the vendor name as the headline, and a
           single dot-separated meta line carrying everything that describes the
           payment. Amount and date are deliberately NOT in that line - they keep
           the right-hand column so figures stay aligned down the list. */
        function buildRow(item, index, term) {
            var hasZoom = item.RecordId > 0;
            var color = PALETTE[index % PALETTE.length];
            var name = item.Title || '—';

            return (
                '<div class="vas-dssrch-row' + (hasZoom ? '' : ' vas-dssrch-nozoom') + '" data-id="' + VIS.Utility.Util.getValueOfInt(item.RecordId) + '">' +
                    '<div class="vas-dssrch-avatar" style="background:' + color + ';">' + dsEsc(initials(item.Title)) + '</div>' +
                    '<div class="vas-dssrch-info">' +
                        '<div class="vas-dssrch-name">' + highlight(name, term) + '</div>' +
                        '<div class="vas-dssrch-rowmeta">' + rowMeta(item, term) + '</div>' +
                    '</div>' +
                    '<div class="vas-dssrch-meta">' +
                        '<div class="vas-dssrch-amount">' + formatAmount(item.Amount, item.CurSymbol, item.StdPrecision) + '</div>' +
                        '<div class="vas-dssrch-date">' + dsEsc(formatDate(item.DocDate)) + '</div>' +
                    '</div>' +
                '</div>'
            );
        }

        /* status-dot + docNo · document type · bank ****1234 · currency · invoice ·
           status · [Allocated] [Reconciled] - data first, tags last. */
        function rowMeta(item, term) {
            var pieces = [];

            pieces.push(
                '<span class="vas-dssrch-dot" style="background:' + statusColor(item.DocStatus) + ';"></span>' +
                highlight(item.DocumentNo || '', term)
            );

            if (item.DocTypeName) { pieces.push(highlight(item.DocTypeName, term)); }

            var bank = formatBank(item.BankName, item.BankAccountNo);
            if (bank) { pieces.push(highlight(bank, term)); }

            if (item.CurrencyIso) { pieces.push(dsEsc(item.CurrencyIso)); }

            if (item.MatchedInvoiceNo) {
                pieces.push(dsEsc(msg('VAS_DocSearch_Invoice', 'Invoice')) + ": " + highlight(item.MatchedInvoiceNo, term));
            }

            if (item.DocStatus) { pieces.push(dsEsc(statusMeta(item.DocStatus).label)); }

            if (item.IsAllocated) {
                pieces.push('<span class="vas-dssrch-flag">' + dsEsc(msg('VAS_Allocated', 'Allocated')) + '</span>');
            }
            if (item.IsReconciled) {
                pieces.push('<span class="vas-dssrch-flag">' + dsEsc(msg('VAS_Reconciled', 'Reconciled')) + '</span>');
            }

            return pieces.join('<span class="vas-dssrch-detail-sep">&middot;</span>');
        }

        /* Avatar palette, cycled by row index (matches the sibling search widgets). */
        var PALETTE = ['#0083DA', '#019D89', '#D78B10', '#5F4AA6', '#A33F3F', '#2084C4'];

        function initials(name) {
            if (!name) { return '#'; }
            var parts = $.trim(name).split(/\s+/);
            if (parts.length >= 2) { return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase(); }
            return name.substring(0, 2).toUpperCase();
        }

        /* Wrap the first case-insensitive hit of the search term in <mark>. */
        function highlight(text, term) {
            text = text == null ? '' : String(text);
            if (!term) { return dsEsc(text); }
            var idx = text.toLowerCase().indexOf(String(term).toLowerCase());
            if (idx === -1) { return dsEsc(text); }
            return dsEsc(text.slice(0, idx)) +
                '<mark>' + dsEsc(text.slice(idx, idx + term.length)) + '</mark>' +
                dsEsc(text.slice(idx + term.length));
        }

        /* "Bank ****1234" when both halves are known, degrading to whichever is. */
        function formatBank(bankName, accountNo) {
            var bn = $.trim(bankName || '');
            var an = $.trim(accountNo || '');
            var last4 = an.length > 4 ? an.slice(-4) : an;
            if (bn && last4) { return bn + ' ****' + last4; }
            if (bn) { return bn; }
            if (last4) { return '****' + last4; }
            return '';
        }

        function zoomTo(recordId) {
            if (!recordId) { return; }
            closePanel();
            try {
                if ($self.windowNo >= 0) {
                    // Navigate the CURRENT window's grid to the clicked record (no new window).
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": ZOOM_TABLE + "." + ZOOM_TABLE + "_ID=" + recordId,
                        "TabLayout": "Y",
                        "TabIndex": "0"
                    });
                }
                else {
                    // Standalone dashboard - open the AP Payment window on the record.
                    // windowId already comes back with the search payload, so the util
                    // normally has nothing left to resolve.
                    VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", recordId, windowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                        .done(function (id) {
                            if (id > 0) { windowId = id; }
                        });
                }
            } catch (e) { /* zoom is best-effort */ }
        }

        /* ---- Panel helpers ---- */
        function openPanel() { positionPanel(); $panel.addClass('vas-dssrch-open'); $bar.addClass('vas-dssrch-bar-focus'); }
        function closePanel() { $panel.removeClass('vas-dssrch-open'); $bar.removeClass('vas-dssrch-bar-focus'); }
        function positionPanel() {
            if (!$bar || !$bar[0]) { return; }
            var rect = $bar[0].getBoundingClientRect();
            $panel.css({ left: Math.round(rect.left) + 'px', top: Math.round(rect.bottom + 6) + 'px', width: Math.round(rect.width) + 'px' });
        }
        function setBusy(on) { $bar.toggleClass('vas-dssrch-busy', !!on); }

        /* ---- Formatters / helpers ---- */
        /* The amount is in the PAYMENT's own currency, so its symbol and precision
           travel with the row. The schema-currency values the payload still carries
           are only a fallback for a payment with no currency on it. */
        /* Symbol sits directly against the number - "Rs289,100.00", no gap - with any
           minus sign ahead of the symbol ("-Rs289,100.00"). */
        function formatAmount(number, symbol, precision) {
            var n = (typeof number === 'number') ? number : parseFloat(number);
            if (isNaN(n)) { return ''; }
            var prec = (typeof precision === 'number' && precision >= 0) ? precision : (stdPrecision || 2);
            var sign = n < 0 ? '-' : '';
            var formatted = Math.abs(n).toLocaleString(window.navigator.language, { minimumFractionDigits: prec, maximumFractionDigits: prec });
            var sym = symbol || curSymbol;
            return sign + (sym ? '<span class="vas-dssrch-cur">' + dsEsc(sym) + '</span>' : '') + formatted;
        }
        function formatDate(iso) {
            if (!iso) { return ''; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }
            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }
        function statusMeta(code) {
            switch (String(code).toUpperCase()) {
                case 'CO': return { label: 'Completed',       tone: 'ok',    color: '#019D89' };
                case 'CL': return { label: 'Closed',          tone: 'ok',    color: '#019D89' };
                case 'AP': return { label: 'Approved',        tone: 'info',  color: '#0072C6' };
                case 'DR': return { label: 'Draft',           tone: 'muted', color: '#748494' };
                case 'IP': return { label: 'In Process',      tone: 'warn',  color: '#B5740C' };
                case 'WC': return { label: 'Waiting Confirm', tone: 'warn',  color: '#B5740C' };
                case 'WP': return { label: 'Waiting Payment', tone: 'warn',  color: '#B5740C' };
                case 'NA': return { label: 'Not Approved',    tone: 'err',   color: '#C0392B' };
                case 'IN': return { label: 'Invalid',         tone: 'err',   color: '#C0392B' };
                case 'VO': return { label: 'Voided',          tone: 'err',   color: '#C0392B' };
                case 'RE': return { label: 'Reversed',        tone: 'err',   color: '#C0392B' };
                default:   return { label: code,              tone: 'muted', color: '#748494' };
            }
        }

        /* Saturated colour for the meta dot - the pill backgrounds are too pale
           to read at 7px. */
        function statusColor(code) {
            if (!code) { return '#748494'; }
            return statusMeta(code).color;
        }

        function msg(key, fallback) {
            var value = VIS.Msg.getMsg(key);
            return value && value !== key && value !== '[' + key + ']' ? value : fallback;
        }

        function dsEsc(str) {
            return String(str == null ? '' : str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
        }
        function searchSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>';
        }
        function alertSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';
        }
        function stateHtml(svg, text, isError) {
            return '<div class="vas-dssrch-state' + (isError ? ' vas-dssrch-state-error' : '') + '">' + svg + dsEsc(text) + '</div>';
        }

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'hidden';
            $root.append($bsyDiv);
        }

        this.refreshWidget = function () {
            if (debounceTimer) { window.clearTimeout(debounceTimer); }
            requestSeq++;
            currentTerm = '';
            loadedCount = 0;
            hasMore = false;
            isLoadingMore = false;
            $input.val('');
            $bar.removeClass('vas-dssrch-has-text');
            setBusy(false);
            $panel.empty();
            closePanel();
        };

        this.getRoot = function () { return $root; };

        this._teardown = function () {
            if (debounceTimer) { window.clearTimeout(debounceTimer); }
            if ($self._onDocClick) { document.removeEventListener('mousedown', $self._onDocClick, true); }
            if ($self._onReflow) {
                window.removeEventListener('resize', $self._onReflow, true);
                window.removeEventListener('scroll', $self._onReflow, true);
            }
            if ($panel) { $panel.remove(); }
        };
    };

    VAS.VAS_068_APPaymentSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };
    VAS.VAS_068_APPaymentSearchWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_068_APPaymentSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_068_APPaymentSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_068_APPaymentSearchWidget.prototype.widgetSizeChange = function (widget) { this.widgetInfo = widget; };
    VAS.VAS_068_APPaymentSearchWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
