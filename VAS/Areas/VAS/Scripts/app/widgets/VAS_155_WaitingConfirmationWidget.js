/**
 * Waiting Confirmation Widget (Delivery Order dashboard)
 * Widget number 155.
 * Widget size: 3 columns x 2 rows.
 * Worklist of drafted/in-progress CUSTOMER delivery confirmations
 * (M_InOutConfirm joined to M_InOut with IsSOTrx='Y' AND MovementType='C-',
 * so vendor/receiving confirmations never appear here). A row click opens
 * that confirmation directly (no intermediate list modal): header fields,
 * 5 lines per page, a line click opens the Review Confirmation Line state
 * inside the same modal shell (Scrap Locator / Confirmed Qty / Scrapped
 * Qty / Description editable; DO Line / Product / UOM / Attribute Set
 * Instance / Target Qty / Difference read-only). Footer actions: Mark In
 * Dispute (IsInDispute only, never DocStatus) and Complete Confirmation
 * (standard document engine, DOCACTION_Complete - disabled until every
 * line is fully accounted for). Read-only from the widget's own list view;
 * writes only happen through the three explicit actions above. Plain DOM
 * would work but this widget follows the sibling Delivery Order widgets'
 * jQuery convention for the framework boundary.
 * Backend - VAS_155_WaitingConfirmationWidget/GetWaitingConfirmations
 *           VAS_155_WaitingConfirmationWidget/GetConfirmationDetail
 *           VAS_155_WaitingConfirmationWidget/GetWarehouseLocators
 *           VAS_155_WaitingConfirmationWidget/SaveConfirmationLine
 *           VAS_155_WaitingConfirmationWidget/MarkInDispute
 *           VAS_155_WaitingConfirmationWidget/CompleteConfirmation
 * Summary Message Table
 *  # | Current Text                                     | Message Key
 * ---+--------------------------------------------------+------------------------
 *  1 | Waiting Confirmation                             | VAS_155_WC_Title
 *  2 | Drafted and in-progress delivery confirmations   | VAS_155_WC_Subtitle
 *  3 | 3 Col x 2 Row                                    | VAS_155_WC_SizeBadge
 *  4 | Confirmation / Customer / Lines / Status         | VAS_155_WC_ColConfirmation / VAS_155_WC_ColCustomer / VAS_155_WC_ColLines / VAS_155_WC_ColStatus
 *  5 | Drafted / In Progress / In Dispute               | VAS_155_WC_Drafted / VAS_155_WC_InProgress / VAS_155_WC_InDispute
 *  6 | No drafted or in-progress confirmations          | VAS_155_WC_EmptyState
 *  7 | Loading confirmations...                         | VAS_155_WC_Loading
 *  8 | Unable to load confirmations / Retry             | VAS_155_WC_LoadError / VAS_155_WC_Retry
 *  9 | Showing / of                                     | VAS_Showing / VAS_Of
 * 10 | Previous page / Next page                        | VAS_PreviousPage / VAS_NextPage
 * 11 | Close                                             | Close
 * 12 | Source DO / Customer / Warehouse / Date          | VAS_155_WC_FieldSourceDO / VAS_155_WC_FieldCustomer / VAS_155_WC_FieldWarehouse / VAS_155_WC_FieldDate
 * 13 | Confirmation Lines                               | VAS_155_WC_LinesSectionTitle
 * 14 | Line (line number prepended in code)             | VAS_155_WC_LinePrefix
 * 15 | Matched / Pending                                | VAS_155_WC_Matched / VAS_155_WC_Pending
 * 16 | Loading confirmation lines...                    | VAS_155_WC_LinesLoading
 * 17 | Mark In Dispute / Complete Confirmation           | VAS_155_WC_MarkInDispute / VAS_155_WC_CompleteConfirmation
 * 18 | Complete all confirmation lines before processing the confirmation. | VAS_155_WC_CompleteDisabledHint
 * 19 | Review Confirmation Line                         | VAS_155_WC_ReviewLineTitle
 * 20 | Back to (confirmation number appended in code)   | VAS_155_WC_BackTo
 * 21 | DO Line / UOM / Attribute Set Instance / Scrap Locator | VAS_155_WC_FieldDOLine / VAS_155_WC_FieldUom / VAS_155_WC_FieldAttrSetInstance / VAS_155_WC_FieldScrapLocator
 * 22 | Target Quantity / Confirmed Quantity / Scrapped Quantity / Difference / Description | VAS_155_WC_FieldTargetQty / VAS_155_WC_FieldConfirmedQty / VAS_155_WC_FieldScrappedQty / VAS_155_WC_FieldDifference / VAS_155_WC_FieldDescription
 * 23 | Select a locator                                 | VAS_155_WC_SelectLocator
 * 24 | Save Line                                        | VAS_155_WC_SaveLine
 * 25 | Saving...                                        | VAS_155_WC_Saving
 * 26 | Save failed. / Not found.                        | VAS_155_WC_SaveFailed / VAS_155_WC_NotFound
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

    VAS.VAS_155_WaitingConfirmationWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-wc-root">');
        var $card, $body, $footHelper, $pager, $pageText, $prevButton, $nextButton;
        var $modal, $modalTitleGroup, $modalBody, $modalFooter, $modalClose;
        var listRequest, detailRequest, locatorsRequest;
        var eventNamespace = 'MPCWaitingConfirmation';
        var modalEventNamespace = '.MPCWcModal';
        var lastFocusedEl = null;

        var ROWS_PER_PAGE = 4;
        var LINES_PER_PAGE = 5;

        var STATUS_META = {
            drafted: { msgKey: 'VAS_155_WC_Drafted', fallback: 'Drafted', cls: 'MPC-wc-pill-drafted' },
            inProgress: { msgKey: 'VAS_155_WC_InProgress', fallback: 'In Progress', cls: 'MPC-wc-pill-inprogress' },
            dispute: { msgKey: 'VAS_155_WC_InDispute', fallback: 'In Dispute', cls: 'MPC-wc-pill-dispute' }
        };

        var state = {
            items: [],
            page: 0,
            loaded: false
        };

        var modalState = {
            view: 'detail',        // 'detail' | 'line'
            confirmId: null,
            header: null,
            lines: [],
            linePage: 0,
            lineIdx: null,
            locators: [],
            locatorsLoadedFor: 0,
            saving: false,
            error: ''
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

        function statusPill(statusKey) {
            var meta = STATUS_META[statusKey] || STATUS_META.drafted;
            var pill = el('span', 'MPC-wc-pill ' + meta.cls, lbl(meta.msgKey, meta.fallback));
            return pill;
        }

        function formatDate(value) {
            if (!value) { return ''; }
            var iso = String(value).replace(' ', 'T');
            var date = new Date(iso);
            if (isNaN(date.getTime())) { return String(value); }
            return date.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        function formatQty(value) {
            var num = Number(value || 0);
            return num.toLocaleString(window.navigator.language, { maximumFractionDigits: 6 });
        }

        function parseResponse(text) {
            var data = text;
            try {
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
            } catch (e) { return null; }
            return data || {};
        }

        /* ---- Build the static widget shell once ---- */
        function build() {
            var head = el('div', 'MPC-wc-head');
            var icon = el('span', 'MPC-wc-ico');
            icon.appendChild(svg('<rect width="8" height="4" x="8" y="2" rx="1" ry="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="M12 11h4"/><path d="M12 16h4"/><path d="M8 11h.01"/><path d="M8 16h.01"/>'));
            var titles = el('div', 'MPC-wc-titles');
            titles.appendChild(el('div', 'MPC-wc-title', lbl('VAS_155_WC_Title', 'Waiting Confirmation')));
            titles.appendChild(el('div', 'MPC-wc-subtitle', lbl('VAS_155_WC_Subtitle', 'Drafted and in-progress delivery confirmations')));
            head.appendChild(icon);
            head.appendChild(titles);
            head.appendChild(el('span', 'MPC-wc-badge', lbl('VAS_155_WC_SizeBadge', '3 Col x 2 Row')));

            var gridHead = el('div', 'MPC-wc-row MPC-wc-ghead');
            gridHead.appendChild(el('span', null, lbl('VAS_155_WC_ColConfirmation', 'Confirmation')));
            gridHead.appendChild(el('span', null, lbl('VAS_155_WC_ColCustomer', 'Customer')));
            gridHead.appendChild(el('span', null, lbl('VAS_155_WC_ColLines', 'Lines')));
            gridHead.appendChild(el('span', 'MPC-wc-col-status', lbl('VAS_155_WC_ColStatus', 'Status')));

            $body = $('<div class="MPC-wc-rows"></div>');

            var foot = el('div', 'MPC-wc-foot');
            $footHelper = $('<span class="MPC-wc-helper"></span>');
            $pager = $('<span class="MPC-wc-pager"></span>');
            var prev = el('button', 'MPC-wc-pgbtn');
            prev.type = 'button';
            prev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            var next = el('button', 'MPC-wc-pgbtn');
            next.type = 'button';
            next.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));
            $pageText = $('<span class="MPC-wc-pgtext"></span>');
            $pager.append(prev, $pageText[0], next);
            $(foot).append($footHelper, $pager);

            $prevButton = $(prev);
            $nextButton = $(next);

            $card = $('<div class="MPC-wc-card"></div>');
            $card.append(head, gridHead, $body[0], foot);
            $root.append($card);

            buildModal();

            $prevButton.on('click.' + eventNamespace, function () {
                if (state.page > 0) { state.page--; renderRows(); }
            });
            $nextButton.on('click.' + eventNamespace, function () {
                var pageCount = Math.max(1, Math.ceil(state.items.length / ROWS_PER_PAGE));
                if (state.page < pageCount - 1) { state.page++; renderRows(); }
            });
            $body.on('click.' + eventNamespace, '.MPC-wc-datarow', function () {
                openModal(Number($(this).attr('data-id')), this);
            });
        }

        function buildModal() {
            var $overlay = $('<div class="MPC-wc-overlay" aria-hidden="true"></div>');
            $modal = $('<div class="MPC-wc-modal" role="dialog" aria-modal="true"></div>');

            var $head = $('<div class="MPC-wc-m-head"></div>');
            $modalTitleGroup = $('<div class="MPC-wc-m-titlegroup"></div>');
            $modalClose = $('<button type="button" class="MPC-wc-m-close" aria-label="Close"></button>');
            $modalClose.append(svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>'));
            $head.append($modalTitleGroup, $modalClose);

            $modalBody = $('<div class="MPC-wc-m-body"></div>');
            $modalFooter = $('<div class="MPC-wc-m-foot"></div>');

            $modal.append($head, $modalBody, $modalFooter);
            $overlay.append($modal);
            $('body').append($overlay);

            $modal.data('overlay', $overlay);

            $modalClose.on('click', closeModal);
            $overlay.on('mousedown', function (e) { if (e.target === $overlay[0]) { closeModal(); } });
            $(document).on('keydown' + modalEventNamespace, function (e) {
                if (e.key === 'Escape' && $overlay.hasClass('MPC-wc-open')) { closeModal(); }
            });
        }

        /* ---- Widget list ---- */
        function loadWidgetList() {
            /* ============================================================================
               TEMPORARY FAKE DATA FOR TESTING (M_InOutConfirm is empty in this DB).
               DELETE THIS WHOLE BLOCK (down to "END TEMPORARY FAKE DATA") to restore the
               real GetWaitingConfirmations fetch. status is one of: drafted / inProgress
               / dispute. 5 rows + ROWS_PER_PAGE 3 = 2 pages, so paging is testable too.
               ============================================================================ */
            state.items = [
                { confirmId: -101, confirmNo: 'SC-DEMO-0001', doNo: 'DO-2026-0148', customer: 'Apex Med Systems', lineCount: 3, status: 'drafted' },
                { confirmId: -102, confirmNo: 'SC-DEMO-0002', doNo: 'DO-2026-0147', customer: 'Northwind Energy', lineCount: 5, status: 'inProgress' },
                { confirmId: -103, confirmNo: 'SC-DEMO-0003', doNo: 'DO-2026-0146', customer: 'UrbanAxis Retail', lineCount: 2, status: 'dispute' },
                { confirmId: -104, confirmNo: 'SC-DEMO-0004', doNo: 'DO-2026-0145', customer: 'صيدلية الأرجوان', lineCount: 4, status: 'inProgress' },
                { confirmId: -105, confirmNo: 'SC-DEMO-0005', doNo: 'DO-2026-0144', customer: 'Stelvio Foods', lineCount: 1, status: 'drafted' }
            ];
            state.page = 0;
            state.loaded = true;
            renderRows();
            return;
            /* ===== END TEMPORARY FAKE DATA ===== */

            if (listRequest && typeof listRequest.abort === 'function') {
                try { listRequest.abort(); } catch (ignored) { }
            }
            listRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            showRowsMessage(lbl('VAS_155_WC_Loading', 'Loading confirmations...'));

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/GetWaitingConfirmations';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: listRequest ? listRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showRowsError(); return; }
                state.items = data.rows || [];
                state.page = 0;
                state.loaded = true;
                renderRows();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showRowsError();
            });
        }

        function renderRows() {
            $body.empty();
            var total = state.items.length;

            if (!total) {
                $body.append(el('div', 'MPC-wc-state', lbl('VAS_155_WC_EmptyState', 'No drafted or in-progress confirmations')));
                $footHelper.text('0');
                $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
                $prevButton.prop('disabled', true);
                $nextButton.prop('disabled', true);
                return;
            }

            var pageCount = Math.max(1, Math.ceil(total / ROWS_PER_PAGE));
            if (state.page > pageCount - 1) { state.page = pageCount - 1; }
            var start = state.page * ROWS_PER_PAGE;
            var slice = state.items.slice(start, start + ROWS_PER_PAGE);

            slice.forEach(function (item) { $body.append(buildRow(item)); });

            var from = start + 1;
            var to = Math.min(total, start + ROWS_PER_PAGE);
            $footHelper.text(lbl('VAS_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + lbl('VAS_Of', 'of') + ' ' + total);
            $pageText.text((state.page + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + pageCount);
            $prevButton.prop('disabled', state.page <= 0);
            $nextButton.prop('disabled', state.page >= pageCount - 1);
        }

        function buildRow(item) {
            var row = el('button', 'MPC-wc-row MPC-wc-datarow');
            row.type = 'button';
            row.setAttribute('data-id', item.confirmId);

            row.appendChild(el('span', 'MPC-wc-c-primary', item.confirmNo || ''));
            row.appendChild(el('span', 'MPC-wc-c-body', item.customer || ''));
            row.appendChild(el('span', 'MPC-wc-c-body', String(item.lineCount || 0)));
            var statusCell = el('span', 'MPC-wc-col-status');
            statusCell.appendChild(statusPill(item.status));
            row.appendChild(statusCell);

            return row;
        }

        function showRowsMessage(message) {
            $body.empty();
            $body.append(el('div', 'MPC-wc-state', message));
            $footHelper.text('');
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevButton.prop('disabled', true);
            $nextButton.prop('disabled', true);
        }

        function showRowsError() {
            $body.empty();
            var wrap = el('div', 'MPC-wc-state');
            wrap.appendChild(el('span', null, lbl('VAS_155_WC_LoadError', 'Unable to load confirmations')));
            var retry = el('button', 'MPC-wc-retry', lbl('VAS_155_WC_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', loadWidgetList);
            wrap.appendChild(retry);
            $body.append(wrap);
            $footHelper.text('');
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevButton.prop('disabled', true);
            $nextButton.prop('disabled', true);
        }

        /* ---- Modal: open / close ---- */
        function openModal(confirmId, triggerEl) {
            lastFocusedEl = triggerEl || null;
            modalState = { view: 'detail', confirmId: confirmId, header: null, lines: [], linePage: 0, lineIdx: null, locators: [], locatorsLoadedFor: 0, saving: false, error: '' };

            var $overlay = $modal.data('overlay');
            $overlay.addClass('MPC-wc-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-wc-body-lock');

            showModalMessage(lbl('VAS_155_WC_Loading', 'Loading confirmations...'));
            loadConfirmationDetail();

            $modalClose.focus();
        }

        function closeModal() {
            if (!$modal) { return; }
            var $overlay = $modal.data('overlay');
            $overlay.removeClass('MPC-wc-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-wc-body-lock');
            if (detailRequest && typeof detailRequest.abort === 'function') {
                try { detailRequest.abort(); } catch (ignored) { }
            }
            if (lastFocusedEl && lastFocusedEl.focus) { lastFocusedEl.focus(); }
            lastFocusedEl = null;
        }

        function showModalMessage(message) {
            $modalTitleGroup.empty().append(el('span', 'MPC-wc-m-title', lbl('VAS_155_WC_Title', 'Waiting Confirmation')));
            $modalBody.empty().append(el('div', 'MPC-wc-m-state', message));
            $modalFooter.empty().hide();
        }

        /* ---- Detail view ---- */
        function loadConfirmationDetail() {
            /* ============================================================================
               TEMPORARY FAKE DATA FOR TESTING - DELETE THIS BLOCK (down to "END TEMPORARY
               FAKE DATA") to restore the real GetConfirmationDetail fetch. 12 lines +
               LINES_PER_PAGE 5 = 3 pages; mix of Matched / Pending / zero-confirmed.
               ============================================================================ */
            (function () {
                var src = null;
                for (var s = 0; s < state.items.length; s++) {
                    if (state.items[s].confirmId === modalState.confirmId) { src = state.items[s]; break; }
                }
                var products = ['Facility Sensor Kit', 'Mounting Brackets', 'Calibration Tools', 'Boiler Valves', 'Pressure Gauges', 'Shelving Units', 'Label Printers', 'Barcode Scanners', 'Cold Chain Tags', 'Lab Centrifuge', 'Sample Trays', 'Reagent Boxes'];
                var fakeLines = [];
                for (var i = 0; i < 12; i++) {
                    var target = 5 + i * 3;
                    var confirmed = (i % 3 === 0) ? target : (i % 3 === 1 ? Math.floor(target / 2) : 0);
                    fakeLines.push({ lineNo: i + 1, productName: products[i], confirmedQty: confirmed, targetQty: target, matched: confirmed === target });
                }
                modalState.header = {
                    confirmId: modalState.confirmId,
                    confirmNo: src ? src.confirmNo : 'SC-DEMO',
                    doNo: src ? src.doNo : 'DO-DEMO',
                    customer: src ? src.customer : 'Demo Customer',
                    status: src ? src.status : 'inProgress',
                    warehouseName: 'Central Warehouse - Baghdad',
                    warehouseId: 0,
                    confirmDate: '2026-07-20'
                };
                modalState.lines = fakeLines;
                modalState.view = 'detail';
                modalState.linePage = 0;
                renderModal();
            })();
            return;
            /* ===== END TEMPORARY FAKE DATA ===== */

            if (detailRequest && typeof detailRequest.abort === 'function') {
                try { detailRequest.abort(); } catch (ignored) { }
            }
            detailRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/GetConfirmationDetail?confirmId=' + encodeURIComponent(modalState.confirmId);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: detailRequest ? detailRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error || !data.header) {
                    showModalMessage(lbl('VAS_155_WC_NotFound', 'Not found.'));
                    return;
                }
                modalState.header = data.header;
                modalState.lines = data.lines || [];
                modalState.view = 'detail';
                modalState.linePage = 0;
                renderModal();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showModalMessage(lbl('VAS_155_WC_NotFound', 'Not found.'));
            });
        }

        function renderModal() {
            if (modalState.view === 'line') { renderLineView(); } else { renderDetailView(); }
        }

        function renderDetailView() {
            var header = modalState.header;
            $modalTitleGroup.empty();
            $modalTitleGroup.append(el('span', 'MPC-wc-m-title', (header.confirmNo || '') + ' - ' + (header.doNo || '')));
            $modalTitleGroup.append(statusPill(header.status));

            $modalBody.empty();

            var fieldGrid = el('div', 'MPC-wc-form-grid');
            fieldGrid.appendChild(fieldRO(lbl('VAS_155_WC_FieldSourceDO', 'Source DO'), header.doNo));
            fieldGrid.appendChild(fieldRO(lbl('VAS_155_WC_FieldCustomer', 'Customer'), header.customer));
            fieldGrid.appendChild(fieldRO(lbl('VAS_155_WC_FieldWarehouse', 'Warehouse'), header.warehouseName));
            fieldGrid.appendChild(fieldRO(lbl('VAS_155_WC_FieldDate', 'Date'), formatDate(header.confirmDate)));
            $modalBody.append(fieldGrid);

            $modalBody.append(el('div', 'MPC-wc-section-title', lbl('VAS_155_WC_LinesSectionTitle', 'Confirmation Lines')));

            var lines = modalState.lines;
            var pageCount = Math.max(1, Math.ceil(lines.length / LINES_PER_PAGE));
            if (modalState.linePage > pageCount - 1) { modalState.linePage = pageCount - 1; }
            var start = modalState.linePage * LINES_PER_PAGE;
            var visible = lines.slice(start, start + LINES_PER_PAGE);

            var list = el('div', 'MPC-wc-lines-list');
            visible.forEach(function (line, i) {
                list.appendChild(buildLineRow(line, start + i));
            });
            $modalBody.append(list);

            if (lines.length > LINES_PER_PAGE) {
                var linePager = el('div', 'MPC-wc-line-pager');
                var lFrom = start + 1;
                var lTo = Math.min(lines.length, start + LINES_PER_PAGE);
                linePager.appendChild(el('span', 'MPC-wc-helper', lbl('VAS_Showing', 'Showing') + ' ' + lFrom + '–' + lTo + ' ' + lbl('VAS_Of', 'of') + ' ' + lines.length));
                var pagerBtns = el('span', 'MPC-wc-pager');
                var lPrev = el('button', 'MPC-wc-pgbtn');
                lPrev.type = 'button';
                lPrev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
                lPrev.disabled = modalState.linePage <= 0;
                lPrev.addEventListener('click', function () { if (modalState.linePage > 0) { modalState.linePage--; renderDetailView(); } });
                var lPageText = el('span', 'MPC-wc-pgtext', (modalState.linePage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + pageCount);
                var lNext = el('button', 'MPC-wc-pgbtn');
                lNext.type = 'button';
                lNext.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));
                lNext.disabled = modalState.linePage >= pageCount - 1;
                lNext.addEventListener('click', function () { if (modalState.linePage < pageCount - 1) { modalState.linePage++; renderDetailView(); } });
                pagerBtns.appendChild(lPrev);
                pagerBtns.appendChild(lPageText);
                pagerBtns.appendChild(lNext);
                linePager.appendChild(pagerBtns);
                $modalBody.append(linePager);
            }

            if (modalState.error) {
                $modalBody.append(el('div', 'MPC-wc-m-error', modalState.error));
            }

            $modalFooter.empty().show();
            var disputeBtn = el('button', 'MPC-wc-btn MPC-wc-btn-dispute', lbl('VAS_155_WC_MarkInDispute', 'Mark In Dispute'));
            disputeBtn.type = 'button';
            disputeBtn.disabled = header.status === 'dispute' || modalState.saving;
            disputeBtn.addEventListener('click', markInDispute);

            var allMatched = lines.length > 0 && lines.every(function (l) { return l.matched; });
            var completeBtn = el('button', 'MPC-wc-btn MPC-wc-btn-complete', lbl('VAS_155_WC_CompleteConfirmation', 'Complete Confirmation'));
            completeBtn.type = 'button';
            completeBtn.disabled = !allMatched || modalState.saving;
            if (!allMatched) {
                completeBtn.title = lbl('VAS_155_WC_CompleteDisabledHint', 'Complete all confirmation lines before processing the confirmation.');
            }
            completeBtn.addEventListener('click', completeConfirmation);

            $modalFooter.append(disputeBtn, completeBtn);

            $modalBody.find('.MPC-wc-line-row').on('click', function () {
                openLineReview(Number($(this).attr('data-idx')));
            });
        }

        function fieldRO(label, value) {
            var wrap = el('div', 'MPC-wc-field-ro');
            wrap.appendChild(el('div', 'MPC-wc-flabel', label));
            wrap.appendChild(el('div', 'MPC-wc-fvalue', value && String(value).trim() ? value : '—'));
            return wrap;
        }

        function buildLineRow(line, idx) {
            var row = el('button', 'MPC-wc-line-row');
            row.type = 'button';
            row.setAttribute('data-idx', idx);

            var left = el('span', 'MPC-wc-line-left');
            left.appendChild(el('span', 'MPC-wc-line-name', lbl('VAS_155_WC_LinePrefix', 'Line') + ' ' + line.lineNo));
            left.appendChild(el('span', 'MPC-wc-line-meta', line.productName || ''));

            var right = el('span', 'MPC-wc-line-right');
            right.appendChild(el('span', 'MPC-wc-line-qty', formatQty(line.confirmedQty) + ' / ' + formatQty(line.targetQty)));
            var pill = el('span', 'MPC-wc-pill ' + (line.matched ? 'MPC-wc-pill-matched' : 'MPC-wc-pill-pending'),
                lbl(line.matched ? 'VAS_155_WC_Matched' : 'VAS_155_WC_Pending', line.matched ? 'Matched' : 'Pending'));
            right.appendChild(pill);
            right.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));

            row.appendChild(left);
            row.appendChild(right);
            return row;
        }

        /* ---- Line review state ---- */
        function openLineReview(idx) {
            modalState.view = 'line';
            modalState.lineIdx = idx;
            modalState.error = '';
            renderModal();

            var warehouseId = modalState.header ? modalState.header.warehouseId : 0;
            if (warehouseId > 0 && modalState.locatorsLoadedFor !== warehouseId) {
                loadLocators(warehouseId);
            }
        }

        function loadLocators(warehouseId) {
            if (locatorsRequest && typeof locatorsRequest.abort === 'function') {
                try { locatorsRequest.abort(); } catch (ignored) { }
            }
            locatorsRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/GetWarehouseLocators?warehouseId=' + encodeURIComponent(warehouseId);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: locatorsRequest ? locatorsRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.locators = (data && data.rows) || [];
                modalState.locatorsLoadedFor = warehouseId;
                if (modalState.view === 'line') { renderLineView(); }
            }).catch(function () { /* locator list is best-effort; select stays empty */ });
        }

        function renderLineView() {
            var line = modalState.lines[modalState.lineIdx];
            if (!line) { modalState.view = 'detail'; renderModal(); return; }

            $modalTitleGroup.empty();
            $modalTitleGroup.append(el('span', 'MPC-wc-m-title', lbl('VAS_155_WC_ReviewLineTitle', 'Review Confirmation Line')));

            $modalBody.empty();

            var backLink = el('button', 'MPC-wc-back-link');
            backLink.type = 'button';
            backLink.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            backLink.appendChild(document.createTextNode(lbl('VAS_155_WC_BackTo', 'Back to') + ' ' + (modalState.header.confirmNo || '')));
            backLink.addEventListener('click', function () {
                modalState.view = 'detail';
                modalState.lineIdx = null;
                modalState.error = '';
                renderModal();
            });
            $modalBody.append(backLink);

            var grid = el('div', 'MPC-wc-line-form-grid');

            grid.appendChild(fieldRO(lbl('VAS_155_WC_FieldDOLine', 'DO Line'), line.lineNo + ' - ' + (line.productName || '')));
            grid.appendChild(fieldRO(lbl('VAS_155_WC_FieldUom', 'UOM'), line.uomName));
            grid.appendChild(fieldRO(lbl('VAS_155_WC_FieldAttrSetInstance', 'Attribute Set Instance'), line.attributeSetInstance));

            var locatorField = el('div', 'MPC-wc-field');
            locatorField.appendChild(el('div', 'MPC-wc-flabel', lbl('VAS_155_WC_FieldScrapLocator', 'Scrap Locator')));
            var locatorSelect = document.createElement('select');
            locatorSelect.className = 'MPC-wc-select';
            var emptyOpt = document.createElement('option');
            emptyOpt.value = '0';
            emptyOpt.textContent = lbl('VAS_155_WC_SelectLocator', 'Select a locator');
            locatorSelect.appendChild(emptyOpt);
            modalState.locators.forEach(function (loc) {
                var opt = document.createElement('option');
                opt.value = String(loc.locatorId);
                opt.textContent = loc.locatorName;
                locatorSelect.appendChild(opt);
            });
            locatorSelect.value = String(line.scrapLocatorId || 0);
            locatorField.appendChild(locatorSelect);
            grid.appendChild(locatorField);

            grid.appendChild(fieldRO(lbl('VAS_155_WC_FieldTargetQty', 'Target Quantity'), formatQty(line.targetQty)));

            var confirmedField = el('div', 'MPC-wc-field');
            confirmedField.appendChild(el('div', 'MPC-wc-flabel', lbl('VAS_155_WC_FieldConfirmedQty', 'Confirmed Quantity') + ' *'));
            var confirmedInput = document.createElement('input');
            confirmedInput.type = 'number';
            confirmedInput.min = '0';
            confirmedInput.step = 'any';
            confirmedInput.className = 'MPC-wc-input';
            confirmedInput.value = String(line.confirmedQty != null ? line.confirmedQty : 0);
            confirmedField.appendChild(confirmedInput);
            grid.appendChild(confirmedField);

            var diffField = el('div', 'MPC-wc-field MPC-wc-field-ro');
            diffField.appendChild(el('div', 'MPC-wc-flabel', lbl('VAS_155_WC_FieldDifference', 'Difference')));
            var diffValue = el('div', 'MPC-wc-fvalue', formatQty(line.targetQty - (line.confirmedQty || 0) - (line.scrappedQty || 0)));
            diffField.appendChild(diffValue);
            grid.appendChild(diffField);

            var scrappedField = el('div', 'MPC-wc-field');
            scrappedField.appendChild(el('div', 'MPC-wc-flabel', lbl('VAS_155_WC_FieldScrappedQty', 'Scrapped Quantity')));
            var scrappedInput = document.createElement('input');
            scrappedInput.type = 'number';
            scrappedInput.min = '0';
            scrappedInput.step = 'any';
            scrappedInput.className = 'MPC-wc-input';
            scrappedInput.value = String(line.scrappedQty != null ? line.scrappedQty : 0);
            scrappedField.appendChild(scrappedInput);
            grid.appendChild(scrappedField);

            var descField = el('div', 'MPC-wc-field MPC-wc-field-wide');
            descField.appendChild(el('div', 'MPC-wc-flabel', lbl('VAS_155_WC_FieldDescription', 'Description')));
            var descArea = document.createElement('textarea');
            descArea.className = 'MPC-wc-textarea';
            descArea.value = line.description || '';
            descField.appendChild(descArea);
            grid.appendChild(descField);

            $modalBody.append(grid);

            function updateDiff() {
                var t = Number(line.targetQty) || 0;
                var c = parseFloat(confirmedInput.value) || 0;
                var s = parseFloat(scrappedInput.value) || 0;
                diffValue.textContent = formatQty(t - c - s);
            }
            confirmedInput.addEventListener('input', updateDiff);
            scrappedInput.addEventListener('input', updateDiff);

            if (modalState.error) {
                $modalBody.append(el('div', 'MPC-wc-m-error', modalState.error));
            }

            $modalFooter.empty().show();
            var saveBtn = el('button', 'MPC-wc-btn MPC-wc-btn-complete', lbl(modalState.saving ? 'VAS_155_WC_Saving' : 'VAS_155_WC_SaveLine', modalState.saving ? 'Saving...' : 'Save Line'));
            saveBtn.type = 'button';
            saveBtn.disabled = modalState.saving;
            saveBtn.addEventListener('click', function () {
                saveLine(line, {
                    scrapLocatorId: Number(locatorSelect.value) || 0,
                    confirmedQty: confirmedInput.value,
                    scrappedQty: scrappedInput.value,
                    description: descArea.value
                });
            });
            $modalFooter.append(saveBtn);
        }

        /* ---- Actions ---- */
        function saveLine(line, values) {
            modalState.saving = true;
            modalState.error = '';
            renderLineView();

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/SaveConfirmationLine';
            var body = 'lineConfirmId=' + encodeURIComponent(line.lineConfirmId) +
                '&scrapLocatorId=' + encodeURIComponent(values.scrapLocatorId) +
                '&confirmedQty=' + encodeURIComponent(values.confirmedQty) +
                '&scrappedQty=' + encodeURIComponent(values.scrappedQty) +
                '&description=' + encodeURIComponent(values.description);

            fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: body
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.saving = false;
                if (!data || data.error || data.success === false) {
                    modalState.error = (data && (data.message || data.error)) || lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                    renderLineView();
                    return;
                }
                modalState.view = 'detail';
                modalState.lineIdx = null;
                loadConfirmationDetail();
            }).catch(function () {
                modalState.saving = false;
                modalState.error = lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                renderLineView();
            });
        }

        function markInDispute() {
            if (modalState.saving) { return; }
            modalState.saving = true;
            modalState.error = '';
            renderDetailView();

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/MarkInDispute';
            fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: 'confirmId=' + encodeURIComponent(modalState.confirmId)
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.saving = false;
                if (!data || data.error || data.success === false) {
                    modalState.error = (data && (data.message || data.error)) || lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                    renderDetailView();
                    return;
                }
                loadConfirmationDetail();
                loadWidgetList();
            }).catch(function () {
                modalState.saving = false;
                modalState.error = lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                renderDetailView();
            });
        }

        function completeConfirmation() {
            if (modalState.saving) { return; }
            modalState.saving = true;
            modalState.error = '';
            renderDetailView();

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/CompleteConfirmation';
            fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: 'confirmId=' + encodeURIComponent(modalState.confirmId)
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.saving = false;
                if (!data || data.error || data.success === false) {
                    modalState.error = (data && (data.message || data.error)) || lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                    renderDetailView();
                    return;
                }
                closeModal();
                loadWidgetList();
            }).catch(function () {
                modalState.saving = false;
                modalState.error = lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                renderDetailView();
            });
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();
            loadWidgetList();
        };

        this.refreshWidget = function () {
            closeModal();
            loadWidgetList();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            closeModal();
            if (listRequest && typeof listRequest.abort === 'function') { try { listRequest.abort(); } catch (ignored) { } }
            if (detailRequest && typeof detailRequest.abort === 'function') { try { detailRequest.abort(); } catch (ignored) { } }
            if (locatorsRequest && typeof locatorsRequest.abort === 'function') { try { locatorsRequest.abort(); } catch (ignored) { } }
            $root.off('.' + eventNamespace);
            $body.off('.' + eventNamespace);
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) {
                var $overlay = $modal.data('overlay');
                if ($overlay) { $overlay.remove(); }
                $modal = null;
            }
            $('body').removeClass('MPC-wc-body-lock');
            $root.remove();
            state.items = [];
        };
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
