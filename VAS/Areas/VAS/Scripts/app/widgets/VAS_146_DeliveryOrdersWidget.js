/**
 * Delivery Orders Widget (Delivery Order dashboard)
 * Widget number 146.
 * 6x3 glass list of customer delivery orders (latest first) for ONE selected
 * calendar Month + Year (independent dropdowns, no "All" option, defaulting
 * to the newest DO date in the data). 6 rows per page, client-side paging.
 * A row click opens a read-only modal: DO header, Packages/Gross/Tare summary
 * strip and the line items (Item | Qty | Locator chip) fetched on demand.
 * Backend - VAS_146_DeliveryOrdersWidget/GetDeliveryOrderDates
 *           VAS_146_DeliveryOrdersWidget/GetDeliveryOrders
 *           VAS_146_DeliveryOrdersWidget/GetDeliveryOrderLines
 * Summary Message Table
 *  # | Current Text                                     | Message Key
 * ---+--------------------------------------------------+------------------------
 *  1 | Delivery Orders                                  | VAS_146_DeliveryOrders
 *  2 | Latest first                                     | VAS_146_LatestFirst
 *  3 | orders                                           | VAS_146_Orders
 *  4 | DO Number                                        | VAS_146_DONumber
 *  5 | Customer                                         | VAS_146_Customer
 *  6 | Pkgs                                             | VAS_146_Pkgs
 *  7 | Gross (kg)                                       | VAS_146_GrossKg
 *  8 | Tare (kg)                                        | VAS_146_TareKg
 *  9 | Status                                           | VAS_146_Status
 * 10 | Drafted                                          | VAS_146_Drafted
 * 11 | Completed                                        | VAS_146_Completed
 * 12 | Invoiced                                         | VAS_146_Invoiced
 * 13 | No delivery orders yet.                          | VAS_146_NoOrdersYet
 * 14 | No delivery orders match the selected filters.   | VAS_146_NoOrdersMatch
 * 15 | Packages                                         | VAS_146_Packages
 * 16 | Item                                             | VAS_146_Item
 * 17 | Qty                                              | VAS_146_Qty
 * 18 | Locator                                          | VAS_146_Locator
 * 19 | No line items on this delivery order.            | VAS_146_NoLineItems
 * 20 | Retry                                            | VAS_146_Retry
 * 21 | Filter delivery orders by month                  | VAS_146_FilterByMonth
 * 22 | Filter delivery orders by year                   | VAS_146_FilterByYear
 * 23 | Loading...                                      | Loading
 * 24 | Showing / of                                     | VAS_Showing / VAS_Of
 * 25 | Couldn't load                                    | VAS_CouldntLoad
 * 26 | Previous / Next                                  | VAS_Previous / VAS_Next
 * 27 | Close                                            | Close
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

    VAS.VAS_146_DeliveryOrdersWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-dow-root">');
        var $card;
        var $subCount;
        var $monthSelect;
        var $yearSelect;
        var $tbody;
        var $footHelper;
        var $pageText;
        var $prevButton;
        var $nextButton;
        var $busy;
        var datesRequest;
        var ordersRequest;
        var linesRequest;
        var $modal;
        var $modalNo;
        var $modalChip;
        var $modalSub;
        var $modalSummary;
        var $modalLines;
        var modalEventNamespace = '.MPCDowModal';
        var eventNamespace = 'MPCDeliveryOrders';
        var lastFocusedRow = -1;

        var PAGE_SIZE = 5;
        /* months/years: option lists built from the DO dates, latest first.
           rows: the selected month's orders (server returns one month). */
        var state = { months: [], years: [], month: 0, year: 0, rows: [], page: 0, hasAnyDates: false };

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

        function formatNumber(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        /* "yyyy-MM-dd" -> local Date (parsed by parts; the bare string form
           would be treated as UTC and could shift a day). */
        function parseDate(value) {
            var parts = String(value || '').split('-');
            if (parts.length !== 3) { return null; }
            var date = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
            return isNaN(date.getTime()) ? null : date;
        }

        function formatDate(value) {
            var date = parseDate(value);
            if (!date) { return value || ''; }
            return date.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        function monthName(month) {
            return new Date(2000, month - 1, 1).toLocaleDateString(window.navigator.language, { month: 'short' });
        }

        function statusLabel(code) {
            if (code === 'IV') { return label('VAS_146_Invoiced', 'Invoiced'); }
            if (code === 'CO') { return label('VAS_146_Completed', 'Completed'); }
            return label('VAS_146_Drafted', 'Drafted');
        }

        function statusChipClass(code) {
            if (code === 'IV') { return 'MPC-dow-chip-invoiced'; }
            if (code === 'CO') { return 'MPC-dow-chip-completed'; }
            return 'MPC-dow-chip-drafted';
        }

        function chipHtml(code) {
            return '<span class="MPC-dow-chip ' + statusChipClass(code) + '">' + escapeHtml(statusLabel(code)) + '</span>';
        }

        function setBusy(visible) {
            if ($busy) { $busy.toggleClass('MPC-dow-busy-hidden', !visible); }
        }

        function setPagerDisabled() {
            $prevButton.prop('disabled', true);
            $nextButton.prop('disabled', true);
        }

        /* ---- Filter options (Month / Year, latest first, no "All") ---- */
        function loadFilterOptions() {
            if (datesRequest && datesRequest.readyState !== 4) { datesRequest.abort(); }

            setBusy(true);
            datesRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_146_DeliveryOrdersWidget/GetDeliveryOrderDates',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = parseResponse(response);
                    if (!result || result.error) {
                        setBusy(false);
                        showBodyError(loadFilterOptions);
                        return;
                    }

                    var dates = result.rows || [];
                    state.months = [];
                    state.years = [];
                    for (var i = 0; i < dates.length; i++) {
                        var date = parseDate(dates[i]);
                        if (!date) { continue; }
                        var m = date.getMonth() + 1;
                        var y = date.getFullYear();
                        if (state.months.indexOf(m) < 0) { state.months.push(m); }
                        if (state.years.indexOf(y) < 0) { state.years.push(y); }
                    }

                    state.hasAnyDates = state.months.length > 0 && state.years.length > 0;
                    if (!state.hasAnyDates) {
                        setBusy(false);
                        renderFilters();
                        showEmptyOverall();
                        return;
                    }

                    /* Default = month/year of the newest date (first row). */
                    var newest = parseDate(dates[0]);
                    state.month = newest.getMonth() + 1;
                    state.year = newest.getFullYear();
                    renderFilters();
                    loadOrdersForSelectedMonthYear();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') {
                        setBusy(false);
                        showBodyError(loadFilterOptions);
                    }
                }
            });
        }

        function renderFilters() {
            var monthOptions = '';
            for (var i = 0; i < state.months.length; i++) {
                monthOptions += '<option value="' + state.months[i] + '">' + escapeHtml(monthName(state.months[i])) + '</option>';
            }
            $monthSelect.html(monthOptions).val(String(state.month)).prop('disabled', !state.hasAnyDates);

            var yearOptions = '';
            for (var j = 0; j < state.years.length; j++) {
                yearOptions += '<option value="' + state.years[j] + '">' + state.years[j] + '</option>';
            }
            $yearSelect.html(yearOptions).val(String(state.year)).prop('disabled', !state.hasAnyDates);
        }

        /* ---- Orders of the selected Month + Year ---- */
        function loadOrdersForSelectedMonthYear() {
            if (ordersRequest && ordersRequest.readyState !== 4) { ordersRequest.abort(); }

            setBusy(true);
            $tbody.html('<div class="MPC-dow-state">' + escapeHtml(label('Loading', 'Loading...')) + '</div>');

            ordersRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_146_DeliveryOrdersWidget/GetDeliveryOrders',
                type: 'GET',
                cache: false,
                data: { year: state.year, month: state.month },
                success: function (response) {
                    var result = parseResponse(response);
                    setBusy(false);
                    if (!result || result.error) {
                        showBodyError(loadOrdersForSelectedMonthYear);
                        return;
                    }

                    state.rows = result.rows || [];
                    state.page = 0;
                    renderOrders();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') {
                        setBusy(false);
                        showBodyError(loadOrdersForSelectedMonthYear);
                    }
                }
            });
        }

        function showEmptyOverall() {
            state.rows = [];
            state.page = 0;
            $subCount.text('0');
            $tbody.html('<div class="MPC-dow-state">' + escapeHtml(label('VAS_146_NoOrdersYet', 'No delivery orders yet.')) + '</div>');
            $footHelper.text(label('VAS_Showing', 'Showing') + ' 0 ' + label('VAS_Of', 'of') + ' 0');
            $pageText.text('0 ' + label('VAS_Of', 'of') + ' 0');
            setPagerDisabled();
        }

        function showBodyError(retryAction) {
            state.rows = [];
            state.page = 0;
            $subCount.text('0');
            $tbody.html(
                '<div class="MPC-dow-state">' +
                    '<span>' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</span>' +
                    '<button type="button" class="MPC-dow-retry">' + escapeHtml(label('VAS_146_Retry', 'Retry')) + '</button>' +
                '</div>'
            );
            $tbody.find('.MPC-dow-retry').on('click.' + eventNamespace, retryAction);
            $footHelper.text(label('VAS_Showing', 'Showing') + ' 0 ' + label('VAS_Of', 'of') + ' 0');
            $pageText.text('0 ' + label('VAS_Of', 'of') + ' 0');
            setPagerDisabled();
        }

        function renderOrders() {
            var total = state.rows.length;
            $subCount.text(formatNumber(total));

            if (!total) {
                $tbody.html('<div class="MPC-dow-state">' + escapeHtml(label('VAS_146_NoOrdersMatch', 'No delivery orders match the selected filters.')) + '</div>');
                $footHelper.text(label('VAS_Showing', 'Showing') + ' 0 ' + label('VAS_Of', 'of') + ' 0');
                $pageText.text('0 ' + label('VAS_Of', 'of') + ' 0');
                setPagerDisabled();
                return;
            }

            var totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
            if (state.page > totalPages - 1) { state.page = totalPages - 1; }
            if (state.page < 0) { state.page = 0; }

            var start = state.page * PAGE_SIZE;
            var end = Math.min(start + PAGE_SIZE, total);

            var html = '';
            for (var index = start; index < end; index++) {
                var row = state.rows[index];
                html +=
                    '<button type="button" class="MPC-dow-row MPC-dow-trow" data-index="' + index + '">' +
                        '<span class="MPC-dow-do-cell">' +
                            '<span class="MPC-dow-do-no" title="' + escapeHtml(row.doNumber) + '">' + escapeHtml(row.doNumber) + '</span>' +
                            '<span class="MPC-dow-do-date">' + escapeHtml(formatDate(row.doDate)) + '</span>' +
                        '</span>' +
                        '<span class="MPC-dow-cust" title="' + escapeHtml(row.customerName) + '">' + escapeHtml(row.customerName) + '</span>' +
                        '<span class="MPC-dow-num">' + escapeHtml(formatNumber(row.packageCount)) + '</span>' +
                        '<span class="MPC-dow-num">' + escapeHtml(formatNumber(row.grossWeight)) + '</span>' +
                        '<span class="MPC-dow-num">' + escapeHtml(formatNumber(row.tareWeight)) + '</span>' +
                        '<span class="MPC-dow-chip-cell">' + chipHtml(row.statusCode) + '</span>' +
                    '</button>';
            }
            $tbody.html(html);

            $footHelper.text(label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + label('VAS_Of', 'of') + ' ' + total);
            $pageText.text((state.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + totalPages);
            $prevButton.prop('disabled', state.page === 0);
            $nextButton.prop('disabled', state.page >= totalPages - 1);
        }

        /* ---- Modal ---- */
        function createModal() {
            if ($modal) { return; }

            $modal = $(
                '<div class="MPC-dow-overlay" aria-hidden="true">' +
                    '<div class="MPC-dow-modal" role="dialog" aria-modal="true" tabindex="-1">' +
                        '<div class="MPC-dow-m-head">' +
                            '<div>' +
                                '<div class="MPC-dow-m-title-row">' +
                                    '<span class="MPC-dow-m-no"></span>' +
                                    '<span class="MPC-dow-m-chip"></span>' +
                                '</div>' +
                                '<div class="MPC-dow-m-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="MPC-dow-m-close">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
                            '</button>' +
                        '</div>' +
                        '<div class="MPC-dow-m-summary"></div>' +
                        '<div class="MPC-dow-m-lines">' +
                            '<div class="MPC-dow-lrow MPC-dow-lhead">' +
                                '<span>' + escapeHtml(label('VAS_146_Item', 'Item')) + '</span>' +
                                '<span class="MPC-dow-ta-r">' + escapeHtml(label('VAS_146_Qty', 'Qty')) + '</span>' +
                                '<span>' + escapeHtml(label('VAS_146_Locator', 'Locator')) + '</span>' +
                            '</div>' +
                            '<div class="MPC-dow-m-lines-body"></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $modalNo = $modal.find('.MPC-dow-m-no');
            $modalChip = $modal.find('.MPC-dow-m-chip');
            $modalSub = $modal.find('.MPC-dow-m-sub');
            $modalSummary = $modal.find('.MPC-dow-m-summary');
            $modalLines = $modal.find('.MPC-dow-m-lines-body');

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-dow-m-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + modalEventNamespace, '.MPC-dow-m-close', closeModal);
            $modal.on('click' + modalEventNamespace, function (event) {
                if (event.target === $modal[0]) { closeModal(); }
            });
            $(document).on('keydown' + modalEventNamespace, function (event) {
                if (event.key === 'Escape') { closeModal(); }
            });
        }

        function openModal(index) {
            var row = state.rows[index];
            if (!row) { return; }

            createModal();
            lastFocusedRow = index;

            $modalNo.text(row.doNumber || '');
            $modalChip.attr('class', 'MPC-dow-m-chip MPC-dow-chip ' + statusChipClass(row.statusCode)).text(statusLabel(row.statusCode));
            $modalSub.text((row.customerName || '') + ' · ' + formatDate(row.doDate));
            $modalSummary.html(
                '<div><span class="MPC-dow-m-k">' + escapeHtml(label('VAS_146_Packages', 'Packages')) + '</span><span class="MPC-dow-m-v">' + escapeHtml(formatNumber(row.packageCount)) + '</span></div>' +
                '<div><span class="MPC-dow-m-k">' + escapeHtml(label('VAS_146_GrossKg', 'Gross (kg)')) + '</span><span class="MPC-dow-m-v">' + escapeHtml(formatNumber(row.grossWeight)) + '</span></div>' +
                '<div><span class="MPC-dow-m-k">' + escapeHtml(label('VAS_146_TareKg', 'Tare (kg)')) + '</span><span class="MPC-dow-m-v">' + escapeHtml(formatNumber(row.tareWeight)) + '</span></div>'
            );

            $modal.addClass('MPC-dow-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-dow-body-lock');
            $modal.find('.MPC-dow-m-close').trigger('focus');

            loadOrderLines(row.deliveryOrderId);
        }

        function loadOrderLines(deliveryOrderId) {
            if (linesRequest && linesRequest.readyState !== 4) { linesRequest.abort(); }

            $modalLines.html('<div class="MPC-dow-m-state">' + escapeHtml(label('Loading', 'Loading...')) + '</div>');

            linesRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_146_DeliveryOrdersWidget/GetDeliveryOrderLines',
                type: 'GET',
                cache: false,
                data: { deliveryOrderId: deliveryOrderId },
                success: function (response) {
                    var result = parseResponse(response);
                    if (!result || result.error) {
                        $modalLines.html('<div class="MPC-dow-m-state">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
                        return;
                    }
                    renderModalLines(result.rows || []);
                },
                error: function (xhr, status) {
                    if (status !== 'abort') {
                        $modalLines.html('<div class="MPC-dow-m-state">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
                    }
                }
            });
        }

        function renderModalLines(lines) {
            if (!lines.length) {
                $modalLines.html('<div class="MPC-dow-m-state">' + escapeHtml(label('VAS_146_NoLineItems', 'No line items on this delivery order.')) + '</div>');
                return;
            }

            var html = '';
            for (var index = 0; index < lines.length; index++) {
                var line = lines[index];
                html +=
                    '<div class="MPC-dow-lrow MPC-dow-lbody">' +
                        '<span class="MPC-dow-l-item" title="' + escapeHtml(line.itemName) + '">' + escapeHtml(line.itemName) + '</span>' +
                        '<span class="MPC-dow-l-qty">' + escapeHtml(formatNumber(line.quantity)) + '</span>' +
                        '<span>' + (line.locatorName ? '<span class="MPC-dow-l-loc">' + escapeHtml(line.locatorName) + '</span>' : '-') + '</span>' +
                    '</div>';
            }
            $modalLines.html(html);
        }

        function closeModal() {
            if (linesRequest && linesRequest.readyState !== 4) { linesRequest.abort(); }
            if (!$modal) { return; }
            $modal.removeClass('MPC-dow-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-dow-body-lock');
            /* Restore focus to the row that opened the modal (accessibility). */
            if (lastFocusedRow >= 0) {
                var $row = $tbody.find('.MPC-dow-trow[data-index="' + lastFocusedRow + '"]');
                if ($row.length) { $row.trigger('focus'); }
                lastFocusedRow = -1;
            }
        }

        this.Initalize = function () {
            var previousLabel = label('VAS_Previous', 'Previous');
            var nextLabel = label('VAS_Next', 'Next');

            $card = $(
                '<div class="MPC-dow-card" aria-live="polite">' +
                    '<div class="MPC-dow-head">' +
                        '<div class="MPC-dow-head-left">' +
                            '<span class="MPC-dow-ico" aria-hidden="true">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                    '<path d="M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2"/>' +
                                    '<path d="M15 18H9"/>' +
                                    '<path d="M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.624l-3.48-4.35A1 1 0 0 0 17.52 8H14"/>' +
                                    '<circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/>' +
                                '</svg>' +
                            '</span>' +
                            '<span class="MPC-dow-titles">' +
                                '<span class="MPC-dow-title">' + escapeHtml(label('VAS_146_DeliveryOrders', 'Delivery Orders')) + '</span>' +
                                '<span class="MPC-dow-sub">' + escapeHtml(label('VAS_146_LatestFirst', 'Latest first')) + ' · <span></span> ' + escapeHtml(label('VAS_146_Orders', 'orders')) + '</span>' +
                            '</span>' +
                        '</div>' +
                        '<div class="MPC-dow-filters">' +
                            '<select class="MPC-dow-select MPC-dow-month" aria-label="' + escapeHtml(label('VAS_146_FilterByMonth', 'Filter delivery orders by month')) + '"></select>' +
                            '<select class="MPC-dow-select MPC-dow-year" aria-label="' + escapeHtml(label('VAS_146_FilterByYear', 'Filter delivery orders by year')) + '"></select>' +
                        '</div>' +
                    '</div>' +
                    '<div class="MPC-dow-row MPC-dow-thead">' +
                        '<span>' + escapeHtml(label('VAS_146_DONumber', 'DO Number')) + '</span>' +
                        '<span>' + escapeHtml(label('VAS_146_Customer', 'Customer')) + '</span>' +
                        '<span class="MPC-dow-ta-r">' + escapeHtml(label('VAS_146_Pkgs', 'Pkgs')) + '</span>' +
                        '<span class="MPC-dow-ta-r">' + escapeHtml(label('VAS_146_GrossKg', 'Gross (kg)')) + '</span>' +
                        '<span class="MPC-dow-ta-r">' + escapeHtml(label('VAS_146_TareKg', 'Tare (kg)')) + '</span>' +
                        '<span>' + escapeHtml(label('VAS_146_Status', 'Status')) + '</span>' +
                    '</div>' +
                    '<div class="MPC-dow-tbody"></div>' +
                    '<div class="MPC-dow-foot">' +
                        '<span class="MPC-dow-helper"></span>' +
                        '<span class="MPC-dow-pager">' +
                            '<button type="button" class="MPC-dow-pgbtn MPC-dow-prev">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg>' +
                            '</button>' +
                            '<span class="MPC-dow-pgtext"></span>' +
                            '<button type="button" class="MPC-dow-pgbtn MPC-dow-next">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>' +
                            '</button>' +
                        '</span>' +
                    '</div>' +
                    '<div class="vis-busyindicatorouterwrap MPC-dow-busy MPC-dow-busy-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>'
            );

            $subCount = $card.find('.MPC-dow-sub span');
            $monthSelect = $card.find('.MPC-dow-month');
            $yearSelect = $card.find('.MPC-dow-year');
            $tbody = $card.find('.MPC-dow-tbody');
            $footHelper = $card.find('.MPC-dow-helper');
            $pageText = $card.find('.MPC-dow-pgtext');
            $busy = $card.find('.MPC-dow-busy');
            $prevButton = $card.find('.MPC-dow-prev').attr({ 'aria-label': previousLabel, title: previousLabel });
            $nextButton = $card.find('.MPC-dow-next').attr({ 'aria-label': nextLabel, title: nextLabel });

            modalEventNamespace += '-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $monthSelect.on('change.' + eventNamespace, function () {
                state.month = Number($(this).val()) || state.month;
                loadOrdersForSelectedMonthYear();
            });
            $yearSelect.on('change.' + eventNamespace, function () {
                state.year = Number($(this).val()) || state.year;
                loadOrdersForSelectedMonthYear();
            });
            $prevButton.on('click.' + eventNamespace, function () {
                if (state.page > 0) { state.page--; renderOrders(); }
            });
            $nextButton.on('click.' + eventNamespace, function () {
                var totalPages = Math.ceil(state.rows.length / PAGE_SIZE);
                if (state.page < totalPages - 1) { state.page++; renderOrders(); }
            });
            $root.on('click.' + eventNamespace, '.MPC-dow-trow', function () {
                openModal(Number($(this).attr('data-index')));
            });

            $root.append($card);
            loadFilterOptions();
        };

        this.refreshWidget = function () {
            closeModal();
            loadFilterOptions();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            closeModal();
            if (datesRequest && datesRequest.readyState !== 4) { datesRequest.abort(); }
            if (ordersRequest && ordersRequest.readyState !== 4) { ordersRequest.abort(); }
            $root.off('.' + eventNamespace);
            if ($monthSelect) { $monthSelect.off('.' + eventNamespace); }
            if ($yearSelect) { $yearSelect.off('.' + eventNamespace); }
            if ($prevButton) { $prevButton.off('.' + eventNamespace); }
            if ($nextButton) { $nextButton.off('.' + eventNamespace); }
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
            state.rows = [];
        };
    };

    VAS.VAS_146_DeliveryOrdersWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_146_DeliveryOrdersWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_146_DeliveryOrdersWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_146_DeliveryOrdersWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
