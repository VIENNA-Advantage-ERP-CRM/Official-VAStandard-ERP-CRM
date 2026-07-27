/**
 * DO Invoicing Status Widget (Delivery Order dashboard)
 * Widget number 143.
 * Widget size: 3 columns x 2 rows.
 * Period-scoped (Month/Quarter/FY) invoice status for customer delivery
 * orders: three stat blocks (Raised / Completed / Open Balance) with
 * count + compact monetary meta, a coverage progress bar (invoiced value
 * vs. period DO value), and a footer helper (DO count + trend % vs the
 * prior equivalent period) with a Month/Quarter/FY period switcher. Every
 * number comes from one aggregate summary call - never computed client-side
 * from raw invoice rows. A stat block click opens a drill-down modal
 * (Invoice | Against | Customer | Amount | Status) scoped to that category
 * + the active period, server-paginated with an adaptive page size computed
 * from the modal's actual rendered height (never a scrollbar). Read-only.
 * Backend - VAS_143_DOInvoicingWidget/GetSummary
 *           VAS_143_DOInvoicingWidget/GetDrillDown
 * Summary Message Table
 *  # | Current Text                                     | Message Key
 * ---+--------------------------------------------------+------------------------
 *  1 | DO Invoicing Status                              | VAS_143_DIS_Title
 *  2 | All delivery orders (period label appended in code) | VAS_143_DIS_Subtitle
 *  3 | Invoices Raised / Completed / Open Balance       | VAS_143_DIS_Raised / VAS_143_DIS_Completed / VAS_143_DIS_OpenBalance
 *  4 | billed / settled / pending (compact value appended in code) | VAS_143_DIS_Billed / VAS_143_DIS_Settled / VAS_143_DIS_Pending
 *  5 | Invoiced vs DO value (period label appended in code) | VAS_143_DIS_CoverageLabel
 *  6 | DOs (count prepended in code)                    | VAS_143_DIS_DOsWord
 *  7 | vs prior month / vs prior quarter / vs prior year | VAS_143_DIS_VsPriorMonth / VAS_143_DIS_VsPriorQuarter / VAS_143_DIS_VsPriorYear
 *  8 | Month / Quarter / FY                             | VAS_143_DIS_Month / VAS_143_DIS_Quarter / VAS_143_DIS_FY
 *  9 | Invoices Raised / Invoices Completed / Open Balance Invoices | VAS_143_DIS_ModalTitleRaised / VAS_143_DIS_ModalTitleCompleted / VAS_143_DIS_ModalTitleOpen
 * 10 | All delivery orders                              | VAS_143_DIS_ModalContext
 * 11 | Invoice / Against / Customer / Amount / Status   | VAS_143_DIS_ColInvoice / VAS_143_DIS_ColAgainst / VAS_143_DIS_ColCustomer / VAS_143_DIS_ColAmount / VAS_143_DIS_ColStatus
 * 12 | Completed / Raised / Partially Paid              | VAS_143_DIS_StatusCompleted / VAS_143_DIS_StatusRaised / VAS_143_DIS_StatusPartial
 * 13 | No invoices in this period.                      | VAS_143_DIS_EmptyState
 * 14 | Showing / of                                     | VAS_Showing / VAS_Of
 * 15 | Previous page / Next page                        | VAS_PreviousPage / VAS_NextPage
 * 16 | Couldn't load / Close                             | VAS_CouldntLoad / Close
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

    VAS.VAS_143_DOInvoicingWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-dis-root">');
        var $card, $subtitle;
        var $blockRaisedCount, $blockRaisedMeta, $blockCompletedCount, $blockCompletedMeta, $blockOpenCount, $blockOpenMeta;
        var $coverageLabel, $coveragePct, $coverageBar;
        var $footHelper, $prevPeriodBtn, $nextPeriodBtn, $periodLabels;
        var $modal, $modalTitle, $modalSubtitle, $modalClose, $modalBody, $modalFootHelper, $modalPager, $modalPageText, $modalPrev, $modalNext;
        var summaryRequest, drillRequest;
        var eventNamespace = 'MPCDoInvoicingStatus';
        var modalEventNamespace = '.MPCDisModal';
        var lastFocusedEl = null;
        var modalResizeTimer = null;

        var PERIODS = ['month', 'quarter', 'fy'];
        var PERIOD_MSG = {
            month: { msgKey: 'VAS_143_DIS_Month', fallback: 'Month' },
            quarter: { msgKey: 'VAS_143_DIS_Quarter', fallback: 'Quarter' },
            fy: { msgKey: 'VAS_143_DIS_FY', fallback: 'FY' }
        };
        var STATUS_META = {
            'Completed': { msgKey: 'VAS_143_DIS_StatusCompleted', fallback: 'Completed', cls: 'MPC-dis-pill-completed' },
            'Raised': { msgKey: 'VAS_143_DIS_StatusRaised', fallback: 'Raised', cls: 'MPC-dis-pill-raised' },
            'Partially Paid': { msgKey: 'VAS_143_DIS_StatusPartial', fallback: 'Partially Paid', cls: 'MPC-dis-pill-partial' }
        };

        // Fixed 5 rows per page (min == max disables the adaptive sizing so the
        // drill-down always pages 5 at a time).
        var MIN_PAGE_SIZE = 5;
        var MAX_PAGE_SIZE = 5;
        var ESTIMATED_ROW_HEIGHT = 46;

        var state = {
            period: 'month',
            summary: null,
            loaded: false
        };

        var modalState = {
            open: false,
            category: 'raised',
            page: 0,
            pageSize: 5,
            total: 0,
            rows: [],
            measured: false
        };

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function el(tag, className, text) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            if (text != null) { node.textContent = text; }
            return node;
        }

        function svg(paths) {
            var span = document.createElement('span');
            span.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + paths + '</svg>';
            return span.firstChild;
        }

        function parseResponse(text) {
            var data = text;
            try {
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
            } catch (e) { return null; }
            return data || {};
        }

        // System currency (returned by the controller from the session base
        // currency). Replaces the hardcoded '$'. Token prefers ISO, falls back to
        // symbol - works for IQD, INR (₹), USD, etc.
        var currency = {};
        function curPrefix() {
            var token = currency.iso || currency.symbol || '';
            return token ? token + ' ' : '';
        }

        function formatCompact(value) {
            var num = Number(value || 0);
            var abs = Math.abs(num);
            var p = curPrefix();
            if (abs >= 1000000) { return p + (num / 1000000).toFixed(2) + 'M'; }
            if (abs >= 1000) { return p + Math.round(num / 1000) + 'K'; }
            return p + Math.round(num);
        }

        function formatFull(value) {
            var num = Number(value || 0);
            return curPrefix() + num.toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        function formatDate(value) {
            if (!value) { return '—'; }
            var iso = String(value).replace(' ', 'T');
            var date = new Date(iso);
            if (isNaN(date.getTime())) { return String(value); }
            return date.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        /* ---- Build the static widget shell once ---- */
        function build() {
            var head = el('div', 'MPC-dis-head');
            var icon = el('span', 'MPC-dis-ico');
            icon.appendChild(svg('<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/><path d="M10 9H8"/><path d="M16 13H8"/><path d="M16 17H8"/>'));
            var titles = el('div', 'MPC-dis-titles');
            titles.appendChild(el('div', 'MPC-dis-title', lbl('VAS_143_DIS_Title', 'DO Invoicing Status')));
            $subtitle = $('<div class="MPC-dis-subtitle"></div>');
            titles.appendChild($subtitle[0]);
            head.appendChild(icon);
            head.appendChild(titles);

            var stats = el('div', 'MPC-dis-stats');

            var raisedBlock = buildStatBlock('raised', 'VAS_143_DIS_Raised', 'Invoices Raised', 'MPC-dis-v-raised');
            $blockRaisedCount = raisedBlock.$count;
            $blockRaisedMeta = raisedBlock.$meta;

            var completedBlock = buildStatBlock('completed', 'VAS_143_DIS_Completed', 'Completed', 'MPC-dis-v-completed');
            $blockCompletedCount = completedBlock.$count;
            $blockCompletedMeta = completedBlock.$meta;
            completedBlock.$el.addClass('MPC-dis-divider');

            var openBlock = buildStatBlock('open', 'VAS_143_DIS_OpenBalance', 'Open Balance', 'MPC-dis-v-open');
            $blockOpenCount = openBlock.$count;
            $blockOpenMeta = openBlock.$meta;
            openBlock.$el.addClass('MPC-dis-divider');

            stats.appendChild(raisedBlock.$el[0]);
            stats.appendChild(completedBlock.$el[0]);
            stats.appendChild(openBlock.$el[0]);

            var coverage = el('div', 'MPC-dis-coverage');
            var covLine1 = el('div', 'MPC-dis-cov-line1');
            $coverageLabel = $('<span class="MPC-dis-cov-label"></span>');
            $coveragePct = $('<span class="MPC-dis-cov-pct"></span>');
            covLine1.appendChild($coverageLabel[0]);
            covLine1.appendChild($coveragePct[0]);
            var track = el('div', 'MPC-dis-cov-track');
            track.setAttribute('role', 'progressbar');
            track.setAttribute('aria-valuemin', '0');
            track.setAttribute('aria-valuemax', '100');
            $coverageBar = $('<div class="MPC-dis-cov-fill"></div>');
            track.appendChild($coverageBar[0]);
            coverage.appendChild(covLine1);
            coverage.appendChild(track);
            $coverageBar.data('track', track);

            var foot = el('div', 'MPC-dis-foot');
            $footHelper = $('<span class="MPC-dis-foot-helper"></span>');
            var switcher = el('div', 'MPC-dis-switcher');
            $prevPeriodBtn = $('<button type="button" class="MPC-dis-chev" aria-label="Previous period"></button>');
            $prevPeriodBtn.append(svg('<path d="m15 18-6-6 6-6"/>'));
            $periodLabels = $('<span class="MPC-dis-period-labels"></span>');
            PERIODS.forEach(function (p) {
                var meta = PERIOD_MSG[p];
                var btn = $('<button type="button" class="MPC-dis-period-btn"></button>').attr('data-period', p).text(lbl(meta.msgKey, meta.fallback));
                $periodLabels.append(btn);
            });
            $nextPeriodBtn = $('<button type="button" class="MPC-dis-chev" aria-label="Next period"></button>');
            $nextPeriodBtn.append(svg('<path d="m9 18 6-6-6-6"/>'));
            switcher.appendChild($prevPeriodBtn[0]);
            switcher.appendChild($periodLabels[0]);
            switcher.appendChild($nextPeriodBtn[0]);
            $(foot).append($footHelper, switcher);

            $card = $('<div class="MPC-dis-card"></div>');
            $card.append(head, stats, coverage, foot);
            $root.append($card);

            buildModal();

            $root.on('click.' + eventNamespace, '.MPC-dis-stat', function () {
                openModal($(this).attr('data-category'), this);
            });
            $prevPeriodBtn.on('click.' + eventNamespace, function () { stepPeriod(-1); });
            $nextPeriodBtn.on('click.' + eventNamespace, function () { stepPeriod(1); });
            $periodLabels.on('click.' + eventNamespace, '.MPC-dis-period-btn', function () {
                setPeriod($(this).attr('data-period'));
            });
        }

        function buildStatBlock(category, msgKey, fallback, valueClass) {
            var $blockEl = $('<button type="button" class="MPC-dis-stat"></button>').attr({ 'data-category': category, 'aria-haspopup': 'dialog' });
            var $label = $('<span class="MPC-dis-stat-label"></span>').text(lbl(msgKey, fallback));
            var $count = $('<span class="MPC-dis-stat-value ' + valueClass + '">—</span>');
            var $meta = $('<span class="MPC-dis-stat-meta"></span>');
            $blockEl.append($label, $count, $meta);
            return { $el: $blockEl, $count: $count, $meta: $meta };
        }

        function buildModal() {
            var $overlay = $('<div class="MPC-dis-overlay" aria-hidden="true"></div>');
            $modal = $('<div class="MPC-dis-modal" role="dialog" aria-modal="true"></div>');

            var $head = $('<div class="MPC-dis-m-head"></div>');
            var $titleGroup = $('<div class="MPC-dis-m-titlegroup"></div>');
            $modalTitle = $('<div class="MPC-dis-m-title"></div>');
            $modalSubtitle = $('<div class="MPC-dis-m-subtitle"></div>');
            $titleGroup.append($modalTitle, $modalSubtitle);
            $modalClose = $('<button type="button" class="MPC-dis-m-close" aria-label="Close"></button>');
            $modalClose.append(svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>'));
            $head.append($titleGroup, $modalClose);

            $modalBody = $('<div class="MPC-dis-m-body"></div>');

            var $footer = $('<div class="MPC-dis-m-foot"></div>');
            $modalFootHelper = $('<span class="MPC-dis-m-foot-helper"></span>');
            $modalPager = $('<span class="MPC-dis-pager"></span>');
            $modalPrev = $('<button type="button" class="MPC-dis-pgbtn" aria-label="Previous page"></button>');
            $modalPrev.append(svg('<path d="m15 18-6-6 6-6"/>'));
            $modalPageText = $('<span class="MPC-dis-pgtext"></span>');
            $modalNext = $('<button type="button" class="MPC-dis-pgbtn" aria-label="Next page"></button>');
            $modalNext.append(svg('<path d="m9 18 6-6-6-6"/>'));
            $modalPager.append($modalPrev, $modalPageText, $modalNext);
            $footer.append($modalFootHelper, $modalPager);

            $modal.append($head, $modalBody, $footer);
            $overlay.append($modal);
            $('body').append($overlay);
            $modal.data('overlay', $overlay);

            $modalClose.on('click', closeModal);
            $overlay.on('mousedown', function (e) { if (e.target === $overlay[0]) { closeModal(); } });
            $(document).on('keydown' + modalEventNamespace, function (e) {
                if (e.key === 'Escape' && $overlay.hasClass('MPC-dis-open')) { closeModal(); }
            });
            $modalPrev.on('click', function () { if (modalState.page > 0) { modalState.page--; loadDrillDown(); } });
            $modalNext.on('click', function () {
                var pageCount = Math.max(1, Math.ceil(modalState.total / modalState.pageSize));
                if (modalState.page < pageCount - 1) { modalState.page++; loadDrillDown(); }
            });

            $(window).on('resize.' + eventNamespace, function () {
                if (!modalState.open) { return; }
                if (modalResizeTimer) { clearTimeout(modalResizeTimer); }
                modalResizeTimer = setTimeout(recomputeModalPageSize, 120);
            });
        }

        /* ---- Period switching ---- */
        function stepPeriod(direction) {
            var idx = PERIODS.indexOf(state.period);
            var next = idx + direction;
            if (next < 0 || next >= PERIODS.length) { return; }
            setPeriod(PERIODS[next]);
        }

        function setPeriod(period) {
            if (PERIODS.indexOf(period) < 0 || period === state.period) { return; }
            state.period = period;
            loadSummary();
        }

        function updatePeriodChrome() {
            $prevPeriodBtn.prop('disabled', state.period === PERIODS[0]);
            $nextPeriodBtn.prop('disabled', state.period === PERIODS[PERIODS.length - 1]);
            $periodLabels.find('.MPC-dis-period-btn').each(function () {
                $(this).toggleClass('MPC-dis-period-active', $(this).attr('data-period') === state.period);
            });
        }

        /* ---- Summary ---- */
        function loadSummary() {
            if (summaryRequest && typeof summaryRequest.abort === 'function') {
                try { summaryRequest.abort(); } catch (ignored) { }
            }
            summaryRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            updatePeriodChrome();

            var url = VIS.Application.contextUrl + 'VAS_143_DOInvoicingWidget/GetSummary?period=' + encodeURIComponent(state.period);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: summaryRequest ? summaryRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showSummaryError(); return; }
                currency = data.currency || currency;
                state.summary = data;
                state.loaded = true;
                renderSummary();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showSummaryError();
            });
        }

        function renderSummary() {
            var s = state.summary;

            $subtitle.text(lbl('VAS_143_DIS_Subtitle', 'All delivery orders') + ' · ' + s.periodLabel);

            $blockRaisedCount.text(String(s.raisedCount));
            $blockRaisedMeta.text(formatCompact(s.raisedValue) + ' ' + lbl('VAS_143_DIS_Billed', 'billed'));

            $blockCompletedCount.text(String(s.completedCount));
            $blockCompletedMeta.text(formatCompact(s.completedValue) + ' ' + lbl('VAS_143_DIS_Settled', 'settled'));

            $blockOpenCount.text(String(s.openCount));
            $blockOpenMeta.text(formatCompact(s.openValue) + ' ' + lbl('VAS_143_DIS_Pending', 'pending'));

            var coveragePct = Math.max(0, Math.min(100, Number(s.coveragePct) || 0));
            $coverageLabel.text(lbl('VAS_143_DIS_CoverageLabel', 'Invoiced vs DO value') + ' (' + s.periodLabel + ')');
            $coveragePct.text(coveragePct + '%');
            $coverageBar.css('width', coveragePct + '%');
            var $track = $coverageBar.data('track');
            $track.setAttribute('aria-valuenow', String(coveragePct));
            $track.setAttribute('aria-label', lbl('VAS_143_DIS_CoverageLabel', 'Invoiced vs DO value') + ' ' + coveragePct + '%');

            var trendFragment = '';
            if (s.trendPct != null) {
                var sign = s.trendPct > 0 ? '+' : '';
                trendFragment = ' · ' + sign + s.trendPct + '% ' + s.trendLabel;
            }
            $footHelper.text(s.doCount + ' ' + lbl('VAS_143_DIS_DOsWord', 'DOs') + trendFragment);
        }

        function showSummaryError() {
            if (state.loaded) {
                // Keep last good data visible; surface a small inline note only.
                $footHelper.text($footHelper.text() + ' · ' + lbl('VAS_CouldntLoad', "Couldn't load"));
                return;
            }
            $blockRaisedCount.text('—');
            $blockCompletedCount.text('—');
            $blockOpenCount.text('—');
            $footHelper.text(lbl('VAS_CouldntLoad', "Couldn't load"));
        }

        /* ---- Modal ---- */
        function openModal(category, triggerEl) {
            lastFocusedEl = triggerEl || null;
            modalState = { open: true, category: category, page: 0, pageSize: modalState.pageSize || 5, total: 0, rows: [], measured: false };

            var $overlay = $modal.data('overlay');
            $overlay.addClass('MPC-dis-open').attr('aria-hidden', 'false');

            var titleKeys = {
                raised: ['VAS_143_DIS_ModalTitleRaised', 'Invoices Raised'],
                completed: ['VAS_143_DIS_ModalTitleCompleted', 'Invoices Completed'],
                open: ['VAS_143_DIS_ModalTitleOpen', 'Open Balance Invoices']
            };
            var t = titleKeys[category] || titleKeys.raised;
            $modalTitle.text(lbl(t[0], t[1]));
            $modalSubtitle.text(state.summary ? state.summary.periodLabel : '');

            showModalMessage('');
            loadDrillDown();

            $modalClose.focus();
        }

        function closeModal() {
            if (!$modal) { return; }
            var $overlay = $modal.data('overlay');
            $overlay.removeClass('MPC-dis-open').attr('aria-hidden', 'true');
            modalState.open = false;
            if (drillRequest && typeof drillRequest.abort === 'function') {
                try { drillRequest.abort(); } catch (ignored) { }
            }
            if (lastFocusedEl && lastFocusedEl.focus) { lastFocusedEl.focus(); }
            lastFocusedEl = null;
        }

        function showModalMessage(message) {
            if (message) {
                $modalBody.empty().append(el('div', 'MPC-dis-m-state', message));
            }
        }

        function loadDrillDown() {
            if (drillRequest && typeof drillRequest.abort === 'function') {
                try { drillRequest.abort(); } catch (ignored) { }
            }
            drillRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            var url = VIS.Application.contextUrl + 'VAS_143_DOInvoicingWidget/GetDrillDown' +
                '?category=' + encodeURIComponent(modalState.category) +
                '&period=' + encodeURIComponent(state.period) +
                '&page=' + encodeURIComponent(modalState.page) +
                '&size=' + encodeURIComponent(modalState.pageSize);

            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: drillRequest ? drillRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) {
                    showModalMessage(lbl('VAS_CouldntLoad', "Couldn't load"));
                    return;
                }
                currency = data.currency || currency;
                modalState.total = data.total || 0;
                modalState.rows = data.rows || [];
                renderModalRows();
                if (!modalState.measured) {
                    modalState.measured = true;
                    window.setTimeout(recomputeModalPageSize, 0);
                }
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showModalMessage(lbl('VAS_CouldntLoad', "Couldn't load"));
            });
        }

        function renderModalRows() {
            var rows = modalState.rows;
            var total = modalState.total;

            if (!rows.length) {
                $modalBody.empty().append(el('div', 'MPC-dis-m-state', lbl('VAS_143_DIS_EmptyState', 'No invoices in this period.')));
                $modalFootHelper.text(lbl('VAS_Showing', 'Showing') + ' 0–0 ' + lbl('VAS_Of', 'of') + ' 0');
                $modalPageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
                $modalPrev.prop('disabled', true);
                $modalNext.prop('disabled', true);
                return;
            }

            var grid = el('div', 'MPC-dis-grid');
            grid.appendChild(el('div', 'MPC-dis-gh', lbl('VAS_143_DIS_ColInvoice', 'invoice')));
            grid.appendChild(el('div', 'MPC-dis-gh', lbl('VAS_143_DIS_ColAgainst', 'against')));
            grid.appendChild(el('div', 'MPC-dis-gh MPC-dis-col-customer', lbl('VAS_143_DIS_ColCustomer', 'customer')));
            grid.appendChild(el('div', 'MPC-dis-gh MPC-dis-align-right', lbl('VAS_143_DIS_ColAmount', 'amount')));
            grid.appendChild(el('div', 'MPC-dis-gh MPC-dis-align-right', lbl('VAS_143_DIS_ColStatus', 'status')));

            rows.forEach(function (row) {
                var invoiceCell = el('div', 'MPC-dis-gc');
                invoiceCell.appendChild(el('div', 'MPC-dis-cell-primary', row.invoiceNo || ''));
                invoiceCell.appendChild(el('div', 'MPC-dis-cell-sub', formatDate(row.invoiceDate)));
                grid.appendChild(invoiceCell);

                var againstCell = el('div', 'MPC-dis-gc');
                againstCell.appendChild(el('div', 'MPC-dis-cell-body', row.refNo || '—'));
                againstCell.appendChild(el('div', 'MPC-dis-cell-sub', formatDate(row.refDate)));
                grid.appendChild(againstCell);

                grid.appendChild(el('div', 'MPC-dis-gc MPC-dis-col-customer', row.customerName || '—'));
                grid.appendChild(el('div', 'MPC-dis-gc MPC-dis-align-right MPC-dis-cell-amount', formatFull(row.amount)));

                var statusMeta = STATUS_META[row.status] || STATUS_META['Raised'];
                var statusCell = el('div', 'MPC-dis-gc MPC-dis-align-right');
                statusCell.appendChild(el('span', 'MPC-dis-pill ' + statusMeta.cls, lbl(statusMeta.msgKey, statusMeta.fallback)));
                grid.appendChild(statusCell);
            });

            $modalBody.empty().append(grid);

            var from = (modalState.page * modalState.pageSize) + 1;
            var to = Math.min(total, from + rows.length - 1);
            $modalFootHelper.text(lbl('VAS_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + lbl('VAS_Of', 'of') + ' ' + total);
            var pageCount = Math.max(1, Math.ceil(total / modalState.pageSize));
            $modalPageText.text((modalState.page + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + pageCount);
            $modalPrev.prop('disabled', modalState.page <= 0);
            $modalNext.prop('disabled', modalState.page >= pageCount - 1);
        }

        /* Adaptive page size: pageSize = floor((bodyHeight - headerRowHeight) /
           rowHeight), clamped [3,12]. Uses an estimated row height until a real
           row is measured, then re-renders once if the computed size changed. */
        function recomputeModalPageSize() {
            if (!modalState.open) { return; }
            var $body = $modalBody;
            if (!$body.length || !$body[0]) { return; }

            var bodyHeight = $body[0].clientHeight;
            var $headerRow = $body.find('.MPC-dis-gh').first();
            var headerHeight = $headerRow.length ? $headerRow[0].getBoundingClientRect().height : 0;
            var $dataRow = $body.find('.MPC-dis-gc').first();
            var rowHeight = $dataRow.length ? $dataRow[0].getBoundingClientRect().height : ESTIMATED_ROW_HEIGHT;
            if (!rowHeight || rowHeight <= 0) { rowHeight = ESTIMATED_ROW_HEIGHT; }

            var newSize = Math.floor((bodyHeight - headerHeight) / rowHeight);
            newSize = Math.max(MIN_PAGE_SIZE, Math.min(MAX_PAGE_SIZE, newSize || MIN_PAGE_SIZE));

            if (newSize === modalState.pageSize) { return; }

            var firstVisibleIndex = modalState.page * modalState.pageSize;
            modalState.pageSize = newSize;
            modalState.page = Math.floor(firstVisibleIndex / newSize);
            loadDrillDown();
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();
            loadSummary();
        };

        this.refreshWidget = function () {
            closeModal();
            loadSummary();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            closeModal();
            if (summaryRequest && typeof summaryRequest.abort === 'function') { try { summaryRequest.abort(); } catch (ignored) { } }
            if (drillRequest && typeof drillRequest.abort === 'function') { try { drillRequest.abort(); } catch (ignored) { } }
            if (modalResizeTimer) { clearTimeout(modalResizeTimer); }
            $root.off('.' + eventNamespace);
            $(window).off('.' + eventNamespace);
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) {
                var $overlay = $modal.data('overlay');
                if ($overlay) { $overlay.remove(); }
                $modal = null;
            }
            $root.remove();
        };
    };

    VAS.VAS_143_DOInvoicingWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_143_DOInvoicingWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_143_DOInvoicingWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_143_DOInvoicingWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_143_DOInvoicingWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_143_DOInvoicingWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
