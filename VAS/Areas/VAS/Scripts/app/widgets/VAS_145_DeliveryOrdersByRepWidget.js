/**
 * Delivery Orders by Representative Widget (Delivery Order dashboard)
 * Widget number 145.
 * Widget size: 4 columns x 2 rows.
 * Per-representative 100% stacked status-mix bars (In Progress / Draft /
 * Completed / Voided) of outbound customer delivery orders for one selected
 * Month + Year (defaults: current month/year; future months of the current
 * year are disabled). 5 representatives per page, most orders first. A row
 * click opens the drill-down modal listing that representative's delivery
 * orders (DO Number | Customer | Date | Status | Value) with per-currency
 * totals in the footer. Read-only. The representative is strictly
 * M_InOut.SalesRep_ID (resolved to AD_User.Name server-side).
 * Backend - VAS_145_DeliveryOrdersByRepWidget/GetRepresentativeSummary
 *           VAS_145_DeliveryOrdersByRepWidget/GetRepresentativeOrders
 * Summary Message Table
 *  # | Current Text                                     | Message Key
 * ---+--------------------------------------------------+------------------------
 *  1 | Delivery Orders by Representative                | VAS_145_Title
 *  2 | In Progress                                      | VAS_145_InProgress
 *  3 | Draft                                            | VAS_145_Draft
 *  4 | Completed                                        | VAS_145_Completed
 *  5 | Voided                                           | VAS_145_Voided
 *  6 | Filter by month                                  | VAS_145_FilterByMonth
 *  7 | Filter by year                                   | VAS_145_FilterByYear
 *  8 | Delivery Orders                                  | VAS_145_DeliveryOrders
 *  9 | orders                                           | VAS_145_Orders
 * 10 | delivery orders                                  | VAS_145_DeliveryOrdersLower
 * 11 | Total value                                      | VAS_145_TotalValue
 * 12 | Total values                                     | VAS_145_TotalValues
 * 13 | No delivery orders in (period appended in code)  | VAS_145_NoOrdersInPeriod
 * 14 | No delivery orders found for / in (name/period assembled in code) | VAS_145_NoOrdersForRep / VAS_145_NoOrdersForRepIn
 * 15 | View delivery orders for (name appended in code) | VAS_145_ViewOrdersFor
 * 16 | DO Number                                        | VAS_145_DONumber
 * 17 | Customer                                         | VAS_145_Customer
 * 18 | Date                                             | VAS_145_Date
 * 19 | Status                                           | VAS_145_Status
 * 20 | Value                                            | VAS_145_Value
 * 21 | Retry                                            | VAS_145_Retry
 * 22 | total                                            | VAS_145_Total
 * 23 | Showing / of                                     | VAS_Showing / VAS_Of
 * 24 | Couldn't load                                    | VAS_CouldntLoad
 * 25 | Previous page / Next page                        | VAS_PreviousPage / VAS_NextPage
 * 26 | Close                                            | Close
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

    VAS.VAS_145_DeliveryOrdersByRepWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-dor-root">');
        var $card;
        var $monthSelect;
        var $yearSelect;
        var $body;
        var $footer;
        var $footHelper;
        var $pager;
        var $pageText;
        var $prevButton;
        var $nextButton;
        var summaryRequest;
        var ordersRequest;
        var $modal;
        var $modalTitle;
        var $modalSubtitle;
        var $modalRows;
        var $modalFooter;
        var modalEventNamespace = '.MPCDorModal';
        var eventNamespace = 'MPCDeliveryOrdersByRep';
        var lastFocusedRow = -1;

        var modalItem = null;
        var modalOrders = [];
        var modalPage = 0;
        var modalSummaryText = '';
        var MODAL_ROWS_PER_PAGE = 5;

        var ROWS_PER_PAGE = 5;
        /* The ONE status mapping (spec section 5): DR = Draft, CO/CL =
           Completed, VO/RE = Voided, everything else (IP, WC, WP, AP, NA, IN,
           unknown, null) = In Progress. The summary counts arrive already
           bucketed by the matching SQL CASEs; this constant buckets the
           drill-down rows, chips and labels. */
        var STATUS_BUCKETS = [
            { key: 'inProgress', msgKey: 'VAS_145_InProgress', fallback: 'In Progress', seg: 'MPC-dor-seg-inprogress', chip: 'MPC-dor-st-inprogress' },
            { key: 'draft', msgKey: 'VAS_145_Draft', fallback: 'Draft', seg: 'MPC-dor-seg-draft', chip: 'MPC-dor-st-draft' },
            { key: 'completed', msgKey: 'VAS_145_Completed', fallback: 'Completed', seg: 'MPC-dor-seg-completed', chip: 'MPC-dor-st-completed' },
            { key: 'voided', msgKey: 'VAS_145_Voided', fallback: 'Voided', seg: 'MPC-dor-seg-voided', chip: 'MPC-dor-st-voided' }
        ];
        var DRAFT_STATUSES = ['DR'];
        var COMPLETED_STATUSES = ['CO', 'CL'];
        var VOIDED_STATUSES = ['VO', 'RE'];

        var now = new Date();
        var CURRENT_MONTH = now.getMonth() + 1;
        var CURRENT_YEAR = now.getFullYear();
        /* Year options: current year and the previous two (spec section 12 -
           no extra query just to populate the year selector). */
        var YEARS = [CURRENT_YEAR, CURRENT_YEAR - 1, CURRENT_YEAR - 2];

        var state = { month: CURRENT_MONTH, year: CURRENT_YEAR, items: [], page: 0 };

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character];
            });
        }

        function parseResponse(response) {
            var parsed = response;
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            return parsed || {};
        }

        function statusBucket(docStatus) {
            var code = String(docStatus || '').toUpperCase();
            if (DRAFT_STATUSES.indexOf(code) >= 0) { return STATUS_BUCKETS[1]; }
            if (COMPLETED_STATUSES.indexOf(code) >= 0) { return STATUS_BUCKETS[2]; }
            if (VOIDED_STATUSES.indexOf(code) >= 0) { return STATUS_BUCKETS[3]; }
            return STATUS_BUCKETS[0];
        }

        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function currencyLocale(iso) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(iso || '').toUpperCase()) >= 0 ? 'en-IN' : 'en-US';
        }

        /* "ISO 1,234.56" - the ISO code comes from the document currency (or
           the session default); never a hardcoded symbol. */
        function formatValue(value, iso) {
            var amount = Number(value || 0).toLocaleString(currencyLocale(iso), {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
            return (iso ? iso + ' ' : '') + amount;
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 0 });
        }

        function monthName(month) {
            return new Date(2000, month - 1, 1).toLocaleDateString(window.navigator.language, { month: 'short' });
        }

        function periodLabel() {
            return monthName(state.month) + ' ' + state.year;
        }

        function formatDate(value) {
            var parts = String(value || '').split('-');
            if (parts.length !== 3) { return value || ''; }
            var date = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
            if (isNaN(date.getTime())) { return value; }
            return date.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        /* ---- Filters ---- */
        function renderFilterOptions() {
            var monthOptions = '';
            for (var m = 1; m <= 12; m++) {
                monthOptions += '<option value="' + m + '">' + escapeHtml(monthName(m)) + '</option>';
            }
            $monthSelect.html(monthOptions).val(String(state.month));

            var yearOptions = '';
            for (var y = 0; y < YEARS.length; y++) {
                yearOptions += '<option value="' + YEARS[y] + '">' + YEARS[y] + '</option>';
            }
            $yearSelect.html(yearOptions).val(String(state.year));

            clampFutureMonths();
        }

        /* Future months are unselectable while the current year is chosen. */
        function clampFutureMonths() {
            var isCurrentYear = state.year === CURRENT_YEAR;
            $monthSelect.find('option').each(function () {
                var m = Number($(this).val());
                $(this).prop('disabled', isCurrentYear && m > CURRENT_MONTH);
            });
            if (isCurrentYear && state.month > CURRENT_MONTH) {
                state.month = CURRENT_MONTH;
                $monthSelect.val(String(state.month));
            }
        }

        /* ---- Summary (status-mix rows) ---- */
        function loadSummary() {
            if (summaryRequest && summaryRequest.readyState !== 4) { summaryRequest.abort(); }

            closeModal();
            state.page = 0;
            renderSkeleton();

            summaryRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_145_DeliveryOrdersByRepWidget/GetRepresentativeSummary',
                type: 'GET',
                cache: false,
                data: { year: state.year, month: state.month },
                success: function (response) {
                    var result = parseResponse(response);
                    if (!result || result.error) {
                        showSummaryError();
                        return;
                    }
                    state.items = result.items || [];
                    state.page = 0;
                    renderSummary();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showSummaryError(); }
                }
            });
        }

        /* Five skeleton rows with the same height as loaded rows, so the
           widget never resizes while loading. */
        function renderSkeleton() {
            var html = '';
            for (var i = 0; i < ROWS_PER_PAGE; i++) {
                html +=
                    '<div class="MPC-dor-row MPC-dor-skeleton" aria-hidden="true">' +
                        '<span class="MPC-dor-skel MPC-dor-skel-label"></span>' +
                        '<span class="MPC-dor-skel MPC-dor-skel-track"></span>' +
                        '<span class="MPC-dor-skel MPC-dor-skel-total"></span>' +
                    '</div>';
            }
            $body.html(html);
            $footHelper.text('');
            $pager.addClass('MPC-dor-hidden');
        }

        function showSummaryError() {
            state.items = [];
            $body.html(
                '<div class="MPC-dor-state">' +
                    '<span>' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</span>' +
                    '<button type="button" class="MPC-dor-retry">' + escapeHtml(label('VAS_145_Retry', 'Retry')) + '</button>' +
                '</div>'
            );
            $body.find('.MPC-dor-retry').on('click.' + eventNamespace, loadSummary);
            $footHelper.text('');
            $pager.addClass('MPC-dor-hidden');
        }

        function segHtml(bucket, count, total) {
            if (count <= 0) { return ''; }
            return '<span class="MPC-dor-seg ' + bucket.seg + '" style="width:' + ((count / total) * 100).toFixed(1) + '%"></span>';
        }

        function rowAriaLabel(item) {
            /* e.g. "View delivery orders for Diana Morris - 4 In Progress, 2
               Draft, 9 Completed, 1 Voided, 16 total". */
            var counts = [item.inProgressCount, item.draftCount, item.completedCount, item.voidedCount];
            var parts = [];
            for (var i = 0; i < STATUS_BUCKETS.length; i++) {
                parts.push(counts[i] + ' ' + label(STATUS_BUCKETS[i].msgKey, STATUS_BUCKETS[i].fallback));
            }
            return label('VAS_145_ViewOrdersFor', 'View delivery orders for') + ' ' + item.representativeName +
                ' - ' + parts.join(', ') + ', ' + item.totalCount + ' ' + label('VAS_145_Total', 'total');
        }

        function renderSummary() {
            var total = state.items.length;

            if (!total) {
                $body.html('<div class="MPC-dor-state">' + escapeHtml(label('VAS_145_NoOrdersInPeriod', 'No delivery orders in') + ' ' + periodLabel()) + '</div>');
                $footHelper.text('');
                $pager.addClass('MPC-dor-hidden');
                return;
            }

            var pageCount = Math.max(1, Math.ceil(total / ROWS_PER_PAGE));
            if (state.page > pageCount - 1) { state.page = pageCount - 1; }
            if (state.page < 0) { state.page = 0; }

            var start = state.page * ROWS_PER_PAGE;
            var end = Math.min(start + ROWS_PER_PAGE, total);

            var html = '';
            for (var index = start; index < end; index++) {
                var item = state.items[index];
                var counts = [item.inProgressCount, item.draftCount, item.completedCount, item.voidedCount];
                var tooltipParts = [];
                for (var b = 0; b < STATUS_BUCKETS.length; b++) {
                    tooltipParts.push(counts[b] + ' ' + label(STATUS_BUCKETS[b].msgKey, STATUS_BUCKETS[b].fallback));
                }
                html +=
                    '<button type="button" class="MPC-dor-row MPC-dor-mix-row" data-index="' + index + '"' +
                        ' title="' + escapeHtml(tooltipParts.join(', ') + ', ' + item.totalCount + ' ' + label('VAS_145_Total', 'total')) + '"' +
                        ' aria-label="' + escapeHtml(rowAriaLabel(item)) + '">' +
                        '<span class="MPC-dor-mix-label" title="' + escapeHtml(item.representativeName) + '">' + escapeHtml(item.representativeName) + '</span>' +
                        '<span class="MPC-dor-mix-track">' +
                            segHtml(STATUS_BUCKETS[0], item.inProgressCount, item.totalCount) +
                            segHtml(STATUS_BUCKETS[1], item.draftCount, item.totalCount) +
                            segHtml(STATUS_BUCKETS[2], item.completedCount, item.totalCount) +
                            segHtml(STATUS_BUCKETS[3], item.voidedCount, item.totalCount) +
                        '</span>' +
                        '<span class="MPC-dor-mix-total">' + escapeHtml(formatCount(item.totalCount)) + '</span>' +
                    '</button>';
            }
            $body.html(html);

            $footHelper.text(label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + label('VAS_Of', 'of') + ' ' + total);
            $pager.removeClass('MPC-dor-hidden');
            $pageText.text((state.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + pageCount);
            $prevButton.prop('disabled', state.page === 0);
            $nextButton.prop('disabled', state.page >= pageCount - 1);
        }

        /* ---- Drill-down modal ---- */
        function createModal() {
            if ($modal) { return; }

            $modal = $(
                '<div class="MPC-dor-overlay" aria-hidden="true">' +
                    '<div class="MPC-dor-modal" role="dialog" aria-modal="true" aria-labelledby="MPC-dor-modal-title-' + ($self.AD_UserHomeWidgetID || 0) + '">' +
                        '<div class="MPC-dor-m-head">' +
                            '<div>' +
                                '<div class="MPC-dor-m-title" id="MPC-dor-modal-title-' + ($self.AD_UserHomeWidgetID || 0) + '"></div>' +
                                '<div class="MPC-dor-m-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="MPC-dor-m-close">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>' +
                            '</button>' +
                        '</div>' +
                        '<div class="MPC-dor-m-body">' +
                            '<div class="MPC-dor-do-row MPC-dor-do-head">' +
                                '<div>' + escapeHtml(label('VAS_145_DONumber', 'DO Number')) + '</div>' +
                                '<div>' + escapeHtml(label('VAS_145_Customer', 'Customer')) + '</div>' +
                                '<div>' + escapeHtml(label('VAS_145_Date', 'Date')) + '</div>' +
                                '<div>' + escapeHtml(label('VAS_145_Status', 'Status')) + '</div>' +
                                '<div class="MPC-dor-ta-r">' + escapeHtml(label('VAS_145_Value', 'Value')) + '</div>' +
                            '</div>' +
                            '<div class="MPC-dor-m-rows"></div>' +
                        '</div>' +
                        '<div class="MPC-dor-m-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $modalTitle = $modal.find('.MPC-dor-m-title');
            $modalSubtitle = $modal.find('.MPC-dor-m-sub');
            $modalRows = $modal.find('.MPC-dor-m-rows');
            $modalFooter = $modal.find('.MPC-dor-m-foot');

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-dor-m-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + modalEventNamespace, '.MPC-dor-m-close', closeModal);
            $modal.on('click' + modalEventNamespace, function (event) {
                if (event.target === $modal[0]) { closeModal(); }
            });
            $(document).on('keydown' + modalEventNamespace, function (event) {
                if (event.key === 'Escape' && $modal && $modal.hasClass('MPC-dor-open')) { closeModal(); }
            });
        }

        function openModal(index) {
            var item = state.items[index];
            if (!item) { return; }

            createModal();
            lastFocusedRow = index;

            $modalTitle.text(label('VAS_145_DeliveryOrders', 'Delivery Orders') + ' · ' + (item.representativeName || ''));
            $modalSubtitle.text(periodLabel() + ' · ' + formatCount(item.totalCount) + ' ' + label('VAS_145_Orders', 'orders'));
            $modalFooter.text('');

            $modal.addClass('MPC-dor-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-dor-body-lock');
            $modal.find('.MPC-dor-m-close').trigger('focus');

            loadRepresentativeOrders(item);
        }

        function modalSkeleton() {
            var html = '';
            for (var i = 0; i < 5; i++) {
                html += '<div class="MPC-dor-do-row MPC-dor-m-skel-row" aria-hidden="true"><span class="MPC-dor-skel MPC-dor-m-skel"></span></div>';
            }
            return html;
        }

        function loadRepresentativeOrders(item) {
            if (ordersRequest && ordersRequest.readyState !== 4) { ordersRequest.abort(); }

            $modalRows.html(modalSkeleton());

            ordersRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_145_DeliveryOrdersByRepWidget/GetRepresentativeOrders',
                type: 'GET',
                cache: false,
                data: { year: state.year, month: state.month, representativeId: item.representativeId },
                success: function (response) {
                    var result = parseResponse(response);
                    if (!result || result.error) {
                        showModalError(item);
                        return;
                    }
                    renderModalRows(item, result.items || []);
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showModalError(item); }
                }
            });
        }

        function showModalError(item) {
            $modalRows.html(
                '<div class="MPC-dor-m-state">' +
                    '<span>' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</span>' +
                    '<button type="button" class="MPC-dor-retry">' + escapeHtml(label('VAS_145_Retry', 'Retry')) + '</button>' +
                '</div>'
            );
            $modalRows.find('.MPC-dor-retry').on('click' + modalEventNamespace, function () {
                loadRepresentativeOrders(item);
            });
            $modalFooter.text('');
        }

        function renderModalRows(item, orders) {
            modalItem = item;
            modalOrders = orders || [];
            modalPage = 0;

            if (!modalOrders.length) {
                modalSummaryText = '';
                $modalRows.html('<div class="MPC-dor-m-state">' +
                    escapeHtml(label('VAS_145_NoOrdersForRep', 'No delivery orders found for') + ' ' + item.representativeName +
                        ' ' + label('VAS_145_NoOrdersForRepIn', 'in') + ' ' + periodLabel()) +
                    '</div>');
                $modalFooter.empty();
                return;
            }

            /* Never sum unlike currencies: totals are accumulated per ISO code
               and rendered one total per currency in the footer. */
            var totalsByIso = {};
            var isoOrder = [];
            for (var i = 0; i < modalOrders.length; i++) {
                var iso = modalOrders[i].currencyIso || '';
                if (totalsByIso[iso] == null) { totalsByIso[iso] = 0; isoOrder.push(iso); }
                totalsByIso[iso] += Number(modalOrders[i].value || 0);
            }

            var totalTexts = [];
            for (var t = 0; t < isoOrder.length; t++) {
                totalTexts.push(formatValue(totalsByIso[isoOrder[t]], isoOrder[t]));
            }
            var totalsLabel = isoOrder.length > 1
                ? label('VAS_145_TotalValues', 'Total values')
                : label('VAS_145_TotalValue', 'Total value');
            modalSummaryText =
                formatCount(modalOrders.length) + ' ' + label('VAS_145_DeliveryOrdersLower', 'delivery orders') +
                ' · ' + totalsLabel + ' ' + totalTexts.join('; ');

            renderModalPage();
        }

        /* Render the current modal page (fixed 5 rows/page); the rows area keeps
           its full 5-row height regardless of how many rows this page holds. */
        function renderModalPage() {
            var pageCount = Math.max(1, Math.ceil(modalOrders.length / MODAL_ROWS_PER_PAGE));
            if (modalPage > pageCount - 1) { modalPage = pageCount - 1; }
            if (modalPage < 0) { modalPage = 0; }

            var start = modalPage * MODAL_ROWS_PER_PAGE;
            var pageRows = modalOrders.slice(start, start + MODAL_ROWS_PER_PAGE);

            var html = '';
            for (var index = 0; index < pageRows.length; index++) {
                var order = pageRows[index];
                var bucket = statusBucket(order.documentStatus);
                html +=
                    '<div class="MPC-dor-do-row">' +
                        '<div class="MPC-dor-do-id" title="' + escapeHtml(order.deliveryOrderNumber) + '">' + escapeHtml(order.deliveryOrderNumber) + '</div>' +
                        '<div class="MPC-dor-do-cell" title="' + escapeHtml(order.customerName) + '">' + escapeHtml(order.customerName) + '</div>' +
                        '<div class="MPC-dor-do-cell">' + escapeHtml(formatDate(order.documentDate)) + '</div>' +
                        '<div><span class="MPC-dor-do-status ' + bucket.chip + '"><span class="MPC-dor-dot"></span>' + escapeHtml(label(bucket.msgKey, bucket.fallback)) + '</span></div>' +
                        '<div class="MPC-dor-do-amount" title="' + escapeHtml(formatValue(order.value, order.currencyIso)) + '">' + escapeHtml(formatValue(order.value, order.currencyIso)) + '</div>' +
                    '</div>';
            }
            $modalRows.html(html);

            renderModalFooter(pageCount);
        }

        function renderModalFooter(pageCount) {
            var pagerHtml = '';
            if (pageCount > 1) {
                pagerHtml =
                    '<span class="MPC-dor-m-pager">' +
                        '<button type="button" class="MPC-dor-pgbtn MPC-dor-m-prev">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg>' +
                        '</button>' +
                        '<span class="MPC-dor-m-pgtext">' + (modalPage + 1) + ' / ' + pageCount + '</span>' +
                        '<button type="button" class="MPC-dor-pgbtn MPC-dor-m-next">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>' +
                        '</button>' +
                    '</span>';
            }
            $modalFooter.html(
                '<span class="MPC-dor-m-foot-text">' + escapeHtml(modalSummaryText) + '</span>' +
                pagerHtml
            );
            $modalFooter.find('.MPC-dor-m-prev')
                .prop('disabled', modalPage === 0)
                .on('click' + modalEventNamespace, function () {
                    if (modalPage > 0) { modalPage--; renderModalPage(); }
                });
            $modalFooter.find('.MPC-dor-m-next')
                .prop('disabled', modalPage >= pageCount - 1)
                .on('click' + modalEventNamespace, function () {
                    if (modalPage < pageCount - 1) { modalPage++; renderModalPage(); }
                });
        }

        function closeModal() {
            if (ordersRequest && ordersRequest.readyState !== 4) { ordersRequest.abort(); }
            if (!$modal) { return; }
            $modal.removeClass('MPC-dor-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-dor-body-lock');
            if (lastFocusedRow >= 0) {
                var $row = $body.find('.MPC-dor-mix-row[data-index="' + lastFocusedRow + '"]');
                if ($row.length) { $row.trigger('focus'); }
                lastFocusedRow = -1;
            }
        }

        this.Initalize = function () {
            var previousLabel = label('VAS_PreviousPage', 'Previous page');
            var nextLabel = label('VAS_NextPage', 'Next page');

            /* Widget size: 4 columns x 2 rows (the dashboard grid owns the
               outer size; the card only fills its cell). */
            $card = $(
                '<div class="MPC-dor-card" aria-live="polite">' +
                    '<div class="MPC-dor-head">' +
                        '<div class="MPC-dor-head-left">' +
                            '<span class="MPC-dor-ico" aria-hidden="true">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                    '<path d="M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2"/>' +
                                    '<path d="M15 18H9"/>' +
                                    '<path d="M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.624l-3.48-4.35A1 1 0 0 0 17.52 8H14"/>' +
                                    '<circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/>' +
                                '</svg>' +
                            '</span>' +
                            '<span class="MPC-dor-titles">' +
                                '<span class="MPC-dor-title">' + escapeHtml(label('VAS_145_Title', 'Delivery Orders by Representative')) + '</span>' +
                                '<span class="MPC-dor-legend">' +
                                    '<span class="MPC-dor-legend-item"><span class="MPC-dor-sw MPC-dor-seg-inprogress"></span>' + escapeHtml(label('VAS_145_InProgress', 'In Progress')) + '</span>' +
                                    '<span class="MPC-dor-legend-item"><span class="MPC-dor-sw MPC-dor-seg-draft"></span>' + escapeHtml(label('VAS_145_Draft', 'Draft')) + '</span>' +
                                    '<span class="MPC-dor-legend-item"><span class="MPC-dor-sw MPC-dor-seg-completed"></span>' + escapeHtml(label('VAS_145_Completed', 'Completed')) + '</span>' +
                                    '<span class="MPC-dor-legend-item"><span class="MPC-dor-sw MPC-dor-seg-voided"></span>' + escapeHtml(label('VAS_145_Voided', 'Voided')) + '</span>' +
                                '</span>' +
                            '</span>' +
                        '</div>' +
                        '<div class="MPC-dor-filters">' +
                            '<select class="MPC-dor-select MPC-dor-month" aria-label="' + escapeHtml(label('VAS_145_FilterByMonth', 'Filter by month')) + '"></select>' +
                            '<select class="MPC-dor-select MPC-dor-year" aria-label="' + escapeHtml(label('VAS_145_FilterByYear', 'Filter by year')) + '"></select>' +
                        '</div>' +
                    '</div>' +
                    '<div class="MPC-dor-body"></div>' +
                    '<div class="MPC-dor-foot">' +
                        '<span class="MPC-dor-helper"></span>' +
                        '<span class="MPC-dor-pager">' +
                            '<button type="button" class="MPC-dor-pgbtn MPC-dor-prev">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg>' +
                            '</button>' +
                            '<span class="MPC-dor-pgtext"></span>' +
                            '<button type="button" class="MPC-dor-pgbtn MPC-dor-next">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>' +
                            '</button>' +
                        '</span>' +
                    '</div>' +
                '</div>'
            );

            $monthSelect = $card.find('.MPC-dor-month');
            $yearSelect = $card.find('.MPC-dor-year');
            $body = $card.find('.MPC-dor-body');
            $footer = $card.find('.MPC-dor-foot');
            $footHelper = $card.find('.MPC-dor-helper');
            $pager = $card.find('.MPC-dor-pager');
            $pageText = $card.find('.MPC-dor-pgtext');
            $prevButton = $card.find('.MPC-dor-prev').attr({ 'aria-label': previousLabel, title: previousLabel });
            $nextButton = $card.find('.MPC-dor-next').attr({ 'aria-label': nextLabel, title: nextLabel });

            modalEventNamespace += '-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            renderFilterOptions();

            $monthSelect.on('change.' + eventNamespace, function () {
                state.month = Number($(this).val()) || state.month;
                loadSummary();
            });
            $yearSelect.on('change.' + eventNamespace, function () {
                state.year = Number($(this).val()) || state.year;
                clampFutureMonths();
                loadSummary();
            });
            $prevButton.on('click.' + eventNamespace, function () {
                if (state.page > 0) { state.page--; renderSummary(); }
            });
            $nextButton.on('click.' + eventNamespace, function () {
                var pageCount = Math.ceil(state.items.length / ROWS_PER_PAGE);
                if (state.page < pageCount - 1) { state.page++; renderSummary(); }
            });
            $root.on('click.' + eventNamespace, '.MPC-dor-mix-row', function () {
                openModal(Number($(this).attr('data-index')));
            });

            $root.append($card);
            loadSummary();
        };

        this.refreshWidget = function () {
            closeModal();
            loadSummary();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            closeModal();
            if (summaryRequest && summaryRequest.readyState !== 4) { summaryRequest.abort(); }
            $root.off('.' + eventNamespace);
            if ($monthSelect) { $monthSelect.off('.' + eventNamespace); }
            if ($yearSelect) { $yearSelect.off('.' + eventNamespace); }
            if ($prevButton) { $prevButton.off('.' + eventNamespace); }
            if ($nextButton) { $nextButton.off('.' + eventNamespace); }
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
            state.items = [];
        };
    };

    VAS.VAS_145_DeliveryOrdersByRepWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_145_DeliveryOrdersByRepWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_145_DeliveryOrdersByRepWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_145_DeliveryOrdersByRepWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
