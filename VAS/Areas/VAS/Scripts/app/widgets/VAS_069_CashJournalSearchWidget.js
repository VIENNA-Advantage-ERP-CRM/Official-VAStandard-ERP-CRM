/************************************************************
 * Module Name    : VAS
 * Purpose        : Cash Journal Search Widget
 *                  Full-width 9x1 dashboard search bar that searches
 *                  cash journals (C_Cash) and shows the most relevant
 *                  matches in a dropdown. Clicking a result zooms to
 *                  the cash journal record.
 * chronological  : Development
 * Created Date   : 13 June 2026
 * Created by     : Claude (VAS widget pattern)
 *
 * AD_Message keys used (add via System Messages):
 *   VAS_069_Placeholder       => "Search cash journals by no., name, cashbook, description..."
 *   VAS_069_Kind              => "Cash Journal"
 *   VAS_DocSearch_TypeToSearch=> "Type at least 2 characters to search"
 *   VAS_DocSearch_NoResults   => "No matching documents"
 *   VAS_DocSearch_Error       => "Search failed. Please try again."
 *   VAS_DocSearch_Results     => "results"
 *   VAS_DocSearch_Invoice     => "Invoice"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_069_CashJournalSearchWidget = function () {
        // ---- Per-widget configuration ----
        var ENDPOINT      = 'VAS/VAS_069_CashJournalSearchWidget/Search';
        var ZOOM_TABLE    = 'C_Cash';
        var CHIP_CLASS    = 'cash';
        var PLACEHOLDER_K = 'VAS_069_Placeholder';
        var PLACEHOLDER_D = 'Search cash journals by no., name, cashbook, description...';
        var KIND_K        = 'VAS_069_Kind';
        var KIND_D        = 'Cash Journal';

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

            var label = msg(KIND_K, KIND_D);
            var html = '<div class="vas-dssrch-count"></div><div class="vas-dssrch-list">';
            for (var i = 0; i < items.length; i++) {
                html += buildRow(items[i], label);
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
            var label = msg(KIND_K, KIND_D);
            var html = '';
            for (var i = 0; i < items.length; i++) {
                html += buildRow(items[i], label);
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

        function buildRow(item, label) {
            var hasZoom = item.RecordId > 0;
            return (
                '<div class="vas-dssrch-row' + (hasZoom ? '' : ' vas-dssrch-nozoom') + '" data-id="' + VIS.Utility.Util.getValueOfInt(item.RecordId) + '">' +
                    '<span class="vas-dssrch-chip vas-dssrch-chip-' + CHIP_CLASS + '">' + dsEsc(label) + '</span>' +
                    '<div class="vas-dssrch-main">' +
                        '<div class="vas-dssrch-docline">' +
                            '<span class="vas-dssrch-docno">' + dsEsc(item.DocumentNo || '') + '</span>' +
                            statusPill(item.DocStatus) +
                        '</div>' +
                        '<div class="vas-dssrch-title">' + dsEsc(item.Title || '') + '</div>' +
                        invoiceTag(item.MatchedInvoiceNo) +
                    '</div>' +
                    '<div class="vas-dssrch-meta">' +
                        '<div class="vas-dssrch-amount">' + formatAmount(item.Amount) + '</div>' +
                        '<div class="vas-dssrch-date">' + dsEsc(formatDate(item.DocDate)) + '</div>' +
                    '</div>' +
                '</div>'
            );
        }

        // The framework navigates IN-PLACE (no new window) only when the payload's
        // ActionName equals the name of the window currently HOSTING this widget;
        // otherwise it opens a new window. Resolve the host window name from the
        // listener chain and pass it as ActionName.
        function hostWindowName() {
            try {
                var l = $self.listener;
                for (var i = 0; i < 6 && l; i++) {
                    if (l.apanel && l.apanel.gridWindow && l.apanel.gridWindow.getName) {
                        return l.apanel.gridWindow.getName();
                    }
                    if (l.gridWindow && l.gridWindow.getName) {
                        return l.gridWindow.getName();
                    }
                    l = l.listener;
                }
            } catch (e) { }
            return '';
        }

        function zoomTo(recordId) {
            if (!recordId) { return; }
            closePanel();
            // Navigate the CURRENT window's grid to the clicked record (no new window).
            try {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": ZOOM_TABLE + "." + ZOOM_TABLE + "_ID=" + recordId,
                    "TabLayout": "Y",
                    "TabIndex": "0",
                    "ActionName": hostWindowName() || "VAS_CashJournal",
                    "ActionType": "W"
                });
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
        function formatAmount(number) {
            var n = (typeof number === 'number') ? number : parseFloat(number);
            if (isNaN(n)) { return ''; }
            var prec = VIS.Env.getCtx().getStdPrecision() || stdPrecision || 2;
            var formatted = n.toLocaleString(window.navigator.language, { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return (curSymbol ? curSymbol + ' ' : '') + formatted;
        }
        function formatDate(iso) {
            if (!iso) { return ''; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }
            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }
        // Shown only when this result matched because of a LINKED invoice (not the
        // cash journal's own fields) - makes "invoice-driven" hits easy to identify.
        function invoiceTag(invNo) {
            if (!invNo) { return ''; }
            return '<div class="vas-dssrch-invtag">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                    '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>' +
                    '<polyline points="14 2 14 8 20 8"/><line x1="8" y1="13" x2="16" y2="13"/><line x1="8" y1="17" x2="13" y2="17"/>' +
                '</svg>' +
                dsEsc(msg('VAS_DocSearch_Invoice', 'Invoice') + ' ' + invNo) +
            '</div>';
        }

        function statusPill(code) {
            if (!code) { return ''; }
            var m = statusMeta(code);
            return '<span class="vas-dssrch-status vas-dssrch-status-' + m.tone + '">' + dsEsc(m.label) + '</span>';
        }

        function statusMeta(code) {
            switch (String(code).toUpperCase()) {
                case 'CO': return { label: 'Completed',       tone: 'ok' };
                case 'CL': return { label: 'Closed',          tone: 'ok' };
                case 'AP': return { label: 'Approved',        tone: 'info' };
                case 'DR': return { label: 'Draft',           tone: 'muted' };
                case 'IP': return { label: 'In Process',      tone: 'warn' };
                case 'WC': return { label: 'Waiting Confirm', tone: 'warn' };
                case 'WP': return { label: 'Waiting Payment', tone: 'warn' };
                case 'NA': return { label: 'Not Approved',    tone: 'err' };
                case 'IN': return { label: 'Invalid',         tone: 'err' };
                case 'VO': return { label: 'Voided',          tone: 'err' };
                case 'RE': return { label: 'Reversed',        tone: 'err' };
                default:   return { label: code,              tone: 'muted' };
            }
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

    VAS.VAS_069_CashJournalSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };
    VAS.VAS_069_CashJournalSearchWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_069_CashJournalSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_069_CashJournalSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_069_CashJournalSearchWidget.prototype.widgetSizeChange = function (widget) { this.widgetInfo = widget; };
    VAS.VAS_069_CashJournalSearchWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
