/**
 * Delivery Order Search Widget (Delivery Order dashboard)
 * Widget number 144.
 * Widget size: 9 columns x 1 row (transparent cell, centered 80% glass pill).
 * Type-ahead search over outbound customer delivery orders (DR/IP/WC/CO only)
 * across eight facets in fixed priority (DO number, customer, contact,
 * contact phone, location, sales order, rep, line item), capped at 7
 * suggestions. Selecting a result opens the detail modal with Overview and
 * Line Items tabs (4 lines per page, product thumbnails with package
 * fallback, subtotal/tax/total honouring the order's tax-included rule).
 * Backend - VAS_144_DeliveryOrderSearchWidget/SearchDeliveryOrders
 *           VAS_144_DeliveryOrderSearchWidget/GetDeliveryOrder
 * Summary Message Table
 *  # | Current Text                                     | Message Key
 * ---+--------------------------------------------------+------------------------
 *  1 | Search delivery orders by customer, line item... | VAS_144_SearchPlaceholder
 *  2 | Enter opens top match                            | VAS_144_EnterOpensTopMatch
 *  3 | Searching...                                     | VAS_144_Searching
 *  4 | No delivery orders match "..." / Try a customer... (query text appended in code) | VAS_144_NoMatchesHint / VAS_144_NoMatchesHintSuffix
 *  5 | Unable to search delivery orders. Please try...  | VAS_144_SearchError
 *  6 | Unable to load the delivery order. Please try... | VAS_144_DetailError
 *  7 | Delivery Order                                   | VAS_144_DeliveryOrder
 *  8 | Drafted                                          | VAS_144_Drafted
 *  9 | In Progress                                      | VAS_144_InProgress
 * 10 | Waiting Confirmation                             | VAS_144_WaitingConfirmation
 * 11 | Completed                                        | VAS_144_Completed
 * 12 | Overview / Line Items                            | VAS_144_Overview / VAS_144_LineItems
 * 13 | Matched                                          | VAS_144_Matched
 * 14 | DO Number / Customer / Contact / Location /      | VAS_144_FacetDONumber / VAS_144_FacetCustomer /
 *    | Sales Order / Rep / Line Item (facet labels)     | VAS_144_FacetContact / VAS_144_FacetLocation /
 *    |                                                  | VAS_144_FacetSalesOrder / VAS_144_FacetRep / VAS_144_FacetLineItem
 * 15 | Total Value / Lines / Delivery Date              | VAS_144_TotalValue / VAS_144_Lines / VAS_144_DeliveryDate
 * 16 | Contact Person / Contact Phone / Delivery        | VAS_144_ContactPerson / VAS_144_ContactPhone /
 *    | Location / Representative / Ship From / Notes    | VAS_144_DeliveryLocation / VAS_144_Representative /
 *    |                                                  | VAS_144_ShipFrom / VAS_144_DeliveryNotes
 * 17 | Item / Qty / Price / Total                       | VAS_144_Item / VAS_144_Qty / VAS_144_Price / VAS_144_Total
 * 18 | Subtotal / Tax                                   | VAS_144_Subtotal / VAS_144_Tax
 * 19 | Delivery / Pickup / Shipper (via-rule labels)    | VAS_144_ViaDelivery / VAS_144_ViaPickup / VAS_144_ViaShipper
 * 20 | Showing / of                                     | VAS_Showing / VAS_Of
 * 21 | Previous page / Next page / Close / Loading      | VAS_PreviousPage / VAS_NextPage / Close / Loading
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

    VAS.VAS_144_DeliveryOrderSearchWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-dos-root">');
        var $pill;
        var $input;
        var $clearBtn;
        var $dropdown;
        var $dashboardScroll;
        var $modal;
        var $modalTitle;
        var $modalStatus;
        var $modalPanel;
        var searchTimer = null;
        var requestSequence = 0;
        var searchRequest;
        var detailRequest;
        var detailLoading = false;
        var suggestions = [];
        var currentOrder = null;
        var activeTab = 'overview';
        var linePage = 0;
        var eventNamespace = '.MPCDeliveryOrderSearch';

        var LINE_PAGE_SIZE = 4;
        /* The ONE status mapping: code -> label key + pill class. Only these
           four statuses ever reach this widget. */
        var STATUS_MAP = {
            DR: { msgKey: 'VAS_144_Drafted', fallback: 'Drafted', cls: 'MPC-dos-st-dr' },
            IP: { msgKey: 'VAS_144_InProgress', fallback: 'In Progress', cls: 'MPC-dos-st-ip' },
            WC: { msgKey: 'VAS_144_WaitingConfirmation', fallback: 'Waiting Confirmation', cls: 'MPC-dos-st-wc' },
            CO: { msgKey: 'VAS_144_Completed', fallback: 'Completed', cls: 'MPC-dos-st-co' }
        };
        var FACET_MAP = {
            DO: { msgKey: 'VAS_144_FacetDONumber', fallback: 'DO Number' },
            CUST: { msgKey: 'VAS_144_FacetCustomer', fallback: 'Customer' },
            CONTACT: { msgKey: 'VAS_144_FacetContact', fallback: 'Contact' },
            LOC: { msgKey: 'VAS_144_FacetLocation', fallback: 'Location' },
            SO: { msgKey: 'VAS_144_FacetSalesOrder', fallback: 'Sales Order' },
            REP: { msgKey: 'VAS_144_FacetRep', fallback: 'Rep' },
            LINE: { msgKey: 'VAS_144_FacetLineItem', fallback: 'Line Item' }
        };
        var VIA_MAP = {
            D: { msgKey: 'VAS_144_ViaDelivery', fallback: 'Delivery' },
            P: { msgKey: 'VAS_144_ViaPickup', fallback: 'Pickup' },
            S: { msgKey: 'VAS_144_ViaShipper', fallback: 'Shipper' }
        };

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

        function statusOf(code) {
            return STATUS_MAP[String(code || '').toUpperCase()] || STATUS_MAP.DR;
        }

        function statusPillHtml(code, extraClass) {
            var st = statusOf(code);
            return '<span class="MPC-dos-status ' + st.cls + (extraClass ? ' ' + extraClass : '') + '">' + escapeHtml(label(st.msgKey, st.fallback)) + '</span>';
        }

        function facetLabel(code) {
            var facet = FACET_MAP[String(code || '').toUpperCase()];
            return facet ? label(facet.msgKey, facet.fallback) : (code || '');
        }

        /* Carrier display priority: shipper name -> vehicle (+ registration)
           -> translated DeliveryViaRule label -> dash. Never invented. */
        function carrierText(order) {
            if (order.shipperName) { return order.shipperName; }
            if (order.vehicleName) {
                return order.vehicleName + (order.vehicleRegistrationNo ? ' · ' + order.vehicleRegistrationNo : '');
            }
            var via = VIA_MAP[String(order.deliveryViaRule || '').toUpperCase()];
            return via ? label(via.msgKey, via.fallback) : '';
        }

        function formatAmount(value, order) {
            var precision = Number(order && order.currencyPrecision);
            if (!isFinite(precision) || precision < 0) { precision = 2; }
            var code = (order && order.currencyCode) || '';
            try {
                if (code) {
                    return new Intl.NumberFormat(window.navigator.language, {
                        style: 'currency',
                        currency: code,
                        minimumFractionDigits: precision,
                        maximumFractionDigits: precision
                    }).format(Number(value || 0));
                }
            } catch (ignored) { /* unknown ISO code - fall through */ }
            return (code ? code + ' ' : '') + Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
        }

        // Same currency-compaction rule used across the dashboard (see Top 10
        // Highest Selling Products / Shipping Method): Indian-numbering
        // currencies compact as Lakh/Crore, every other currency (including
        // this order's own currencyCode, e.g. IQD) compacts as K/M/B. Small
        // amounts (below the first threshold) still go through formatAmount's
        // exact Intl currency formatting - only large values that would
        // otherwise overflow a tight box (stat tiles, totals, line amounts)
        // get shortened.
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
        function usesIndianNumbering(code) { return INDIAN_NUMBERING_CURRENCIES.indexOf(String(code || '').toUpperCase()) >= 0; }
        function trimZeros(text) { return text.replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1'); }

        function formatAmountCompact(value, order) {
            var n = Number(value || 0);
            var abs = Math.abs(n);
            var code = (order && order.currencyCode) || '';
            var num;
            if (usesIndianNumbering(code)) {
                if (abs >= 1e7) { num = trimZeros((n / 1e7).toFixed(2)) + 'Cr'; }
                else if (abs >= 1e5) { num = trimZeros((n / 1e5).toFixed(2)) + 'L'; }
                else if (abs >= 1e3) { num = trimZeros((n / 1e3).toFixed(1)) + 'K'; }
                else { return formatAmount(value, order); }
            } else {
                if (abs >= 1e9) { num = trimZeros((n / 1e9).toFixed(1)) + 'B'; }
                else if (abs >= 1e6) { num = trimZeros((n / 1e6).toFixed(1)) + 'M'; }
                else if (abs >= 1e3) { num = trimZeros((n / 1e3).toFixed(1)) + 'K'; }
                else { return formatAmount(value, order); }
            }
            return code ? (code + ' ' + num) : num;
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        function formatDate(value) {
            var parts = String(value || '').split('-');
            if (parts.length !== 3) { return value || ''; }
            var date = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
            if (isNaN(date.getTime())) { return value; }
            return date.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        function resolveImageUrl(imageUrl) {
            if (!imageUrl) { return ''; }
            if (imageUrl.indexOf('http') === 0 || imageUrl.indexOf('data:') === 0) { return imageUrl; }
            var contextUrl = (VIS.Application && VIS.Application.contextUrl) || '';
            if (contextUrl && contextUrl.charAt(contextUrl.length - 1) !== '/' && imageUrl.charAt(0) !== '/') {
                return contextUrl + '/' + imageUrl;
            }
            if (contextUrl && contextUrl.charAt(contextUrl.length - 1) === '/' && imageUrl.charAt(0) === '/') {
                return contextUrl + imageUrl.substring(1);
            }
            return contextUrl + imageUrl;
        }

        /* ---- Dropdown ---- */
        function positionDropdown() {
            if (!$dropdown || !$pill) { return; }
            var rect = $pill[0].getBoundingClientRect();
            $dropdown.css({
                left: Math.round(rect.left) + 'px',
                top: Math.round(rect.bottom + 6) + 'px',
                width: Math.round(rect.width) + 'px'
            });
        }

        function openDropdown(html) {
            $dropdown.html(html).addClass('MPC-dos-open');
            positionDropdown();
        }

        function closeDropdown() {
            if ($dropdown) { $dropdown.removeClass('MPC-dos-open'); }
        }

        function dropdownIsOpen() {
            return $dropdown && $dropdown.hasClass('MPC-dos-open');
        }

        function scheduleSearch() {
            if (searchTimer) { clearTimeout(searchTimer); }
            var text = $input.val().trim();
            if (!text) {
                requestSequence++;
                closeDropdown();
                return;
            }
            searchTimer = setTimeout(function () { runSearch(text); }, 250);
        }

        function runSearch(text) {
            var sequence = ++requestSequence;
            openDropdown('<div class="MPC-dos-dd-state">' + escapeHtml(label('VAS_144_Searching', 'Searching...')) + '</div>');

            if (searchRequest && searchRequest.readyState !== 4) { searchRequest.abort(); }
            searchRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_144_DeliveryOrderSearchWidget/SearchDeliveryOrders',
                type: 'GET',
                cache: false,
                data: { q: text },
                success: function (response) {
                    if (sequence !== requestSequence) { return; }
                    var result = parseResponse(response);
                    if (!result || result.error) {
                        openDropdown('<div class="MPC-dos-dd-state">' + escapeHtml(label('VAS_144_SearchError', 'Unable to search delivery orders. Please try again.')) + '</div>');
                        return;
                    }
                    suggestions = result.rows || [];
                    renderSuggestions(text);
                },
                error: function (xhr, status) {
                    if (status === 'abort' || sequence !== requestSequence) { return; }
                    openDropdown('<div class="MPC-dos-dd-state">' + escapeHtml(label('VAS_144_SearchError', 'Unable to search delivery orders. Please try again.')) + '</div>');
                }
            });
        }

        function truckIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<path d="M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2"/><path d="M15 18H9"/>' +
                '<path d="M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.624l-3.48-4.35A1 1 0 0 0 17.52 8H14"/>' +
                '<circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/></svg>';
        }

        function renderSuggestions(text) {
            if (!suggestions.length) {
                openDropdown('<div class="MPC-dos-dd-state">' +
                    escapeHtml(label('VAS_144_NoMatchesHint', 'No delivery orders match') + ' "' + text + '". ' +
                        label('VAS_144_NoMatchesHintSuffix', 'Try a customer, item, sales order, location or rep.')) +
                    '</div>');
                return;
            }

            var html = '';
            for (var index = 0; index < suggestions.length; index++) {
                var row = suggestions[index];
                var secondLine = label('VAS_144_Matched', 'Matched') + ' ' + facetLabel(row.matchedFacet) + ': ' + (row.matchedValue || '-');
                if (row.salesOrder) { secondLine += ' · ' + row.salesOrder; }
                if (row.location) { secondLine += ' · ' + row.location; }
                html +=
                    '<button type="button" class="MPC-dos-dd-row" data-index="' + index + '">' +
                        '<span class="MPC-dos-dd-ico">' + truckIcon() + '</span>' +
                        '<span class="MPC-dos-dd-main">' +
                            '<span class="MPC-dos-dd-top">' +
                                '<span class="MPC-dos-dd-no">' + escapeHtml(row.doNumber) + '</span>' +
                                '<span class="MPC-dos-dd-cust" title="' + escapeHtml(row.customer) + '">' + escapeHtml(row.customer) + '</span>' +
                                statusPillHtml(row.statusCode) +
                            '</span>' +
                            '<span class="MPC-dos-dd-sub" title="' + escapeHtml(secondLine) + '">' + escapeHtml(secondLine) + '</span>' +
                        '</span>' +
                        '<span class="MPC-dos-dd-facet">' + escapeHtml(facetLabel(row.matchedFacet)) + '</span>' +
                    '</button>';
            }
            openDropdown(html);
        }

        function selectSuggestion(index) {
            var row = suggestions[index];
            if (!row || detailLoading) { return; }
            closeDropdown();
            $input.val(row.doNumber + ' - ' + row.customer);
            if ($clearBtn) { $clearBtn.css('display', 'inline-flex'); }
            loadDetail(row.doId);
        }

        /* ---- Detail modal ---- */
        function loadDetail(doId) {
            detailLoading = true;
            if (detailRequest && detailRequest.readyState !== 4) { detailRequest.abort(); }
            detailRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_144_DeliveryOrderSearchWidget/GetDeliveryOrder',
                type: 'GET',
                cache: false,
                data: { doId: doId },
                success: function (response) {
                    detailLoading = false;
                    var result = parseResponse(response);
                    if (!result || result.error || !result.doId) {
                        openDropdown('<div class="MPC-dos-dd-state">' + escapeHtml(label('VAS_144_DetailError', 'Unable to load the delivery order. Please try again.')) + '</div>');
                        return;
                    }
                    currentOrder = result;
                    activeTab = 'overview';
                    linePage = 0;
                    openModal();
                },
                error: function (xhr, status) {
                    detailLoading = false;
                    if (status !== 'abort') {
                        openDropdown('<div class="MPC-dos-dd-state">' + escapeHtml(label('VAS_144_DetailError', 'Unable to load the delivery order. Please try again.')) + '</div>');
                    }
                }
            });
        }

        function createModal() {
            if ($modal) { return; }

            $modal = $(
                '<div class="MPC-dos-overlay" aria-hidden="true">' +
                    '<div class="MPC-dos-modal" role="dialog" aria-modal="true">' +
                        '<div class="MPC-dos-m-head">' +
                            '<span class="MPC-dos-m-title-wrap">' +
                                '<span class="MPC-dos-m-title"></span>' +
                                '<span class="MPC-dos-m-status"></span>' +
                            '</span>' +
                            '<button type="button" class="MPC-dos-m-close">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>' +
                            '</button>' +
                        '</div>' +
                        '<div class="MPC-dos-tabs">' +
                            '<button type="button" class="MPC-dos-tab" data-tab="overview">' + escapeHtml(label('VAS_144_Overview', 'Overview')) + '</button>' +
                            '<button type="button" class="MPC-dos-tab" data-tab="lines">' + escapeHtml(label('VAS_144_LineItems', 'Line Items')) + '</button>' +
                        '</div>' +
                        '<div class="MPC-dos-panel"></div>' +
                    '</div>' +
                '</div>'
            );

            $modalTitle = $modal.find('.MPC-dos-m-title');
            $modalStatus = $modal.find('.MPC-dos-m-status');
            $modalPanel = $modal.find('.MPC-dos-panel');

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-dos-m-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + eventNamespace, '.MPC-dos-m-close', closeModal);
            $modal.on('click' + eventNamespace, function (event) {
                if (event.target === $modal[0]) { closeModal(); }
            });
            $modal.on('click' + eventNamespace, '.MPC-dos-tab', function () {
                activeTab = $(this).attr('data-tab');
                renderPanel();
            });
            $modal.on('click' + eventNamespace, '.MPC-dos-lp-prev', function () {
                if (linePage > 0) { linePage--; renderPanel(); }
            });
            $modal.on('click' + eventNamespace, '.MPC-dos-lp-next', function () {
                var pages = Math.ceil(((currentOrder && currentOrder.lines) || []).length / LINE_PAGE_SIZE);
                if (linePage < pages - 1) { linePage++; renderPanel(); }
            });
            /* Broken/missing image: remove only the img; the fallback tile
               below it stays visible. */
            $modal.on('error' + eventNamespace, '.MPC-dos-li-img', function () {
                $(this).remove();
            });
            /* Minimal focus trap: keep Tab cycling inside the open modal. */
            $modal.on('keydown' + eventNamespace, function (event) {
                if (event.key !== 'Tab') { return; }
                var $focusables = $modal.find('button:visible');
                if (!$focusables.length) { return; }
                var first = $focusables[0];
                var last = $focusables[$focusables.length - 1];
                if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
                else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
            });
        }

        function openModal() {
            createModal();
            $modalTitle.text(label('VAS_144_DeliveryOrder', 'Delivery Order') + ' - ' + (currentOrder.doNumber || ''));
            $modalStatus.html(statusPillHtml(currentOrder.statusCode));
            renderPanel();
            $modal.addClass('MPC-dos-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-dos-body-lock');
            $modal.find('.MPC-dos-m-close').trigger('focus');
        }

        function closeModal() {
            if (!$modal) { return; }
            $modal.removeClass('MPC-dos-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-dos-body-lock');
            currentOrder = null;
            if ($input && $input.length) { $input.trigger('focus'); }
        }

        function renderPanel() {
            if (!currentOrder) { return; }
            $modal.find('.MPC-dos-tab').removeClass('MPC-dos-tab-active')
                .filter('[data-tab="' + activeTab + '"]').addClass('MPC-dos-tab-active');
            $modalPanel.html(activeTab === 'lines' ? linesTabHtml() : overviewTabHtml());
        }

        function fieldHtml(labelText, valueText, span2) {
            var shown = valueText == null || valueText === '' ? '-' : valueText;
            return '<div class="MPC-dos-field' + (span2 ? ' MPC-dos-field-span2' : '') + '">' +
                '<div class="MPC-dos-field-lbl">' + escapeHtml(labelText) + '</div>' +
                '<div class="MPC-dos-field-val" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
            '</div>';
        }

        function overviewTabHtml() {
            var order = currentOrder;
            var carrier = carrierText(order);
            var chips =
                '<span class="MPC-dos-chip">' + escapeHtml(order.doNumber || '') + '</span>' +
                (order.salesOrder ? '<span class="MPC-dos-chip">' + escapeHtml(order.salesOrder) + '</span>' : '') +
                (carrier ? '<span class="MPC-dos-chip">' + escapeHtml(carrier) + '</span>' : '');

            var stats =
                '<span class="MPC-dos-stats">' +
                    '<span class="MPC-dos-stat"><span class="MPC-dos-stat-k">' + escapeHtml(label('VAS_144_TotalValue', 'Total Value')) + '</span><span class="MPC-dos-stat-v" title="' + escapeHtml(formatAmount(order.total, order)) + '">' + escapeHtml(formatAmountCompact(order.total, order)) + '</span></span>' +
                    '<span class="MPC-dos-stat"><span class="MPC-dos-stat-k">' + escapeHtml(label('VAS_144_Lines', 'Lines')) + '</span><span class="MPC-dos-stat-v">' + ((order.lines || []).length) + '</span></span>' +
                    '<span class="MPC-dos-stat"><span class="MPC-dos-stat-k">' + escapeHtml(label('VAS_144_DeliveryDate', 'Delivery Date')) + '</span><span class="MPC-dos-stat-v">' + escapeHtml(formatDate(order.deliveryDate) || '-') + '</span></span>' +
                '</span>';

            var hero =
                '<div class="MPC-dos-hero">' +
                    '<span class="MPC-dos-hero-main">' +
                        '<span class="MPC-dos-hero-name" title="' + escapeHtml(order.customer || '') + '">' + escapeHtml(order.customer || '') + '</span>' +
                        '<span class="MPC-dos-hero-chips">' + chips + '</span>' +
                    '</span>' +
                    stats +
                '</div>';

            var fields =
                '<div class="MPC-dos-fields">' +
                    fieldHtml(label('VAS_144_ContactPerson', 'Contact Person'), order.contactName) +
                    fieldHtml(label('VAS_144_ContactPhone', 'Contact Phone'), order.contactPhone) +
                    fieldHtml(label('VAS_144_DeliveryLocation', 'Delivery Location'), order.location, true) +
                    fieldHtml(label('VAS_144_Representative', 'Representative'), order.representative) +
                    fieldHtml(label('VAS_144_ShipFrom', 'Ship From'), order.warehouse) +
                '</div>';

            var notes = '';
            if (order.notes) {
                notes =
                    '<div class="MPC-dos-notes">' +
                        '<span class="MPC-dos-field-lbl">' + escapeHtml(label('VAS_144_DeliveryNotes', 'Delivery Notes')) + '</span>' +
                        '<span class="MPC-dos-notes-text" title="' + escapeHtml(order.notes) + '">' + escapeHtml(order.notes) + '</span>' +
                    '</div>';
            }

            return hero + fields + notes;
        }

        function packageIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<path d="m21 8-9 5-9-5"/><path d="m3 8 9-5 9 5v8l-9 5-9-5Z"/><path d="M12 13v8"/></svg>';
        }

        function linesTabHtml() {
            var order = currentOrder;
            var lines = order.lines || [];
            var pages = Math.max(1, Math.ceil(lines.length / LINE_PAGE_SIZE));
            if (linePage > pages - 1) { linePage = pages - 1; }
            if (linePage < 0) { linePage = 0; }
            var start = linePage * LINE_PAGE_SIZE;
            var end = Math.min(start + LINE_PAGE_SIZE, lines.length);

            var rows = '';
            for (var index = start; index < end; index++) {
                var line = lines[index];
                var imageUrl = resolveImageUrl(line.imageUrl);
                var priceFull = line.unitPrice == null ? '-' : formatAmount(line.unitPrice, order);
                var totalFull = line.lineDisplayTotal == null ? '-' : formatAmount(line.lineDisplayTotal, order);
                var priceText = line.unitPrice == null ? '-' : formatAmountCompact(line.unitPrice, order);
                var totalText = line.lineDisplayTotal == null ? '-' : formatAmountCompact(line.lineDisplayTotal, order);
                rows +=
                    '<div class="MPC-dos-lrow">' +
                        '<span class="MPC-dos-li">' +
                            '<span class="MPC-dos-li-thumb">' + packageIcon() +
                                (imageUrl ? '<img class="MPC-dos-li-img" src="' + escapeHtml(imageUrl) + '" alt="" loading="lazy" />' : '') +
                            '</span>' +
                            '<span class="MPC-dos-li-main">' +
                                '<span class="MPC-dos-li-name" title="' + escapeHtml(line.productName || '') + '">' + escapeHtml(line.productName || '-') + '</span>' +
                                '<span class="MPC-dos-li-sku" title="' + escapeHtml(line.sku || '') + '">' + escapeHtml(line.sku || '') + '</span>' +
                            '</span>' +
                        '</span>' +
                        '<span class="MPC-dos-l-qty">' + escapeHtml(formatQty(line.qty)) + '</span>' +
                        '<span class="MPC-dos-l-price" title="' + escapeHtml(priceFull) + '">' + escapeHtml(priceText) + '</span>' +
                        '<span class="MPC-dos-l-total" title="' + escapeHtml(totalFull) + '">' + escapeHtml(totalText) + '</span>' +
                    '</div>';
            }

            var pager = '';
            if (lines.length > LINE_PAGE_SIZE) {
                pager =
                    '<div class="MPC-dos-lp">' +
                        '<span class="MPC-dos-lp-helper">' + escapeHtml(label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + label('VAS_Of', 'of') + ' ' + lines.length) + '</span>' +
                        '<span class="MPC-dos-lp-pager">' +
                            '<button type="button" class="MPC-dos-lp-btn MPC-dos-lp-prev" aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '"' + (linePage === 0 ? ' disabled' : '') + '>' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg></button>' +
                            '<span class="MPC-dos-lp-text">' + (linePage + 1) + ' ' + escapeHtml(label('VAS_Of', 'of')) + ' ' + pages + '</span>' +
                            '<button type="button" class="MPC-dos-lp-btn MPC-dos-lp-next" aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '"' + (linePage >= pages - 1 ? ' disabled' : '') + '>' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg></button>' +
                        '</span>' +
                    '</div>';
            }

            var totals =
                '<div class="MPC-dos-totals">' +
                    '<span class="MPC-dos-total-row"><span>' + escapeHtml(label('VAS_144_Subtotal', 'Subtotal')) + '</span><span title="' + escapeHtml(formatAmount(order.subtotal, order)) + '">' + escapeHtml(formatAmountCompact(order.subtotal, order)) + '</span></span>' +
                    '<span class="MPC-dos-total-row"><span>' + escapeHtml(label('VAS_144_Tax', 'Tax')) + '</span><span title="' + escapeHtml(formatAmount(order.tax, order)) + '">' + escapeHtml(formatAmountCompact(order.tax, order)) + '</span></span>' +
                    '<span class="MPC-dos-total-row MPC-dos-total-grand"><span>' + escapeHtml(label('VAS_144_Total', 'Total')) + '</span><span title="' + escapeHtml(formatAmount(order.total, order)) + '">' + escapeHtml(formatAmountCompact(order.total, order)) + '</span></span>' +
                '</div>';

            return '' +
                '<div class="MPC-dos-lhead">' +
                    '<span>' + escapeHtml(label('VAS_144_Item', 'Item')) + '</span>' +
                    '<span class="MPC-dos-ta-r">' + escapeHtml(label('VAS_144_Qty', 'Qty')) + '</span>' +
                    '<span class="MPC-dos-ta-r MPC-dos-l-price">' + escapeHtml(label('VAS_144_Price', 'Price')) + '</span>' +
                    '<span class="MPC-dos-ta-r">' + escapeHtml(label('VAS_144_Total', 'Total')) + '</span>' +
                '</div>' +
                '<div class="MPC-dos-lines">' + rows + '</div>' +
                pager +
                totals;
        }

        /* ---- Widget ---- */
        this.Initalize = function () {
            $pill = $(
                '<div class="MPC-dos-pill">' +
                    '<span class="MPC-dos-ico" aria-hidden="true">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"></circle><path d="m21 21-4.35-4.35"></path></svg>' +
                    '</span>' +
                    '<input type="text" class="MPC-dos-input" autocomplete="off" role="combobox" aria-expanded="false"' +
                        ' aria-label="' + escapeHtml(label('VAS_144_SearchPlaceholder', 'Search delivery orders by customer, line item, location, sales order, rep or contact...')) + '"' +
                        ' placeholder="' + escapeHtml(label('VAS_144_SearchPlaceholder', 'Search delivery orders by customer, line item, location, sales order, rep or contact...')) + '" />' +
                    '<button type="button" class="MPC-dos-clear" aria-label="' + escapeHtml(label('Clear', 'Clear')) + '" style="display:none">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>' +
                    '</button>' +
                    '<span class="MPC-dos-hint">' + escapeHtml(label('VAS_144_EnterOpensTopMatch', 'Enter opens top match')) + '</span>' +
                '</div>'
            );
            $root.append($pill);
            $input = $pill.find('.MPC-dos-input');
            $clearBtn = $pill.find('.MPC-dos-clear');

            $dropdown = $('<div class="MPC-dos-dd" role="listbox">');
            $('body').append($dropdown);

            $input.on('input' + eventNamespace, function () {
                $clearBtn.css('display', $input.val() ? 'inline-flex' : 'none');
                scheduleSearch();
            });
            $input.on('focus' + eventNamespace, function () {
                if ($input.val().trim()) {
                    $clearBtn.css('display', 'inline-flex');
                    scheduleSearch();
                }
            });
            $input.on('keydown' + eventNamespace, function (event) {
                if (event.key === 'Escape') { closeDropdown(); return; }
                if (event.key === 'Enter' && dropdownIsOpen() && suggestions.length) {
                    event.preventDefault();
                    selectSuggestion(0);
                }
            });

            $clearBtn.on('click' + eventNamespace, function () {
                $input.val('');
                $clearBtn.css('display', 'none');
                requestSequence++;
                suggestions = [];
                if (searchTimer) { clearTimeout(searchTimer); }
                if (searchRequest && searchRequest.readyState !== 4) { searchRequest.abort(); }
                closeDropdown();
                $input.focus();
            });

            $dropdown.on('mousedown' + eventNamespace, '.MPC-dos-dd-row', function (event) {
                event.preventDefault();
                selectSuggestion(Number($(this).attr('data-index')));
            });

            $(document).on('mousedown' + eventNamespace, function (event) {
                if (!$(event.target).closest('.MPC-dos-pill, .MPC-dos-dd').length) { closeDropdown(); }
            });
            $(document).on('keydown' + eventNamespace, function (event) {
                if (event.key !== 'Escape') { return; }
                if (dropdownIsOpen()) { closeDropdown(); }
                else if ($modal && $modal.hasClass('MPC-dos-open')) { closeModal(); }
            });
            $(window).on('scroll' + eventNamespace, closeDropdown);
            $(window).on('resize' + eventNamespace, function () {
                if (dropdownIsOpen()) { positionDropdown(); }
            });

            $dashboardScroll = $root.closest('.vis-widget-container, [data-dashboard-container]');
            if ($dashboardScroll.length) {
                $dashboardScroll.on('scroll' + eventNamespace, closeDropdown);
            }
        };

        this.refreshWidget = function () {
            requestSequence++;
            suggestions = [];
            if (searchTimer) { clearTimeout(searchTimer); }
            if ($input) { $input.val(''); }
            if ($clearBtn) { $clearBtn.css('display', 'none'); }
            closeDropdown();
            closeModal();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (searchTimer) { clearTimeout(searchTimer); }
            if (searchRequest && searchRequest.readyState !== 4) { searchRequest.abort(); }
            if (detailRequest && detailRequest.readyState !== 4) { detailRequest.abort(); }
            $(document).off(eventNamespace);
            $(window).off(eventNamespace);
            if ($dashboardScroll && $dashboardScroll.length) { $dashboardScroll.off(eventNamespace); }
            if ($dropdown) { $dropdown.remove(); $dropdown = null; }
            if ($modal) { $modal.remove(); $modal = null; }
            $('body').removeClass('MPC-dos-body-lock');
            $root.remove();
        };
    };

    VAS.VAS_144_DeliveryOrderSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_144_DeliveryOrderSearchWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_144_DeliveryOrderSearchWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_144_DeliveryOrderSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
