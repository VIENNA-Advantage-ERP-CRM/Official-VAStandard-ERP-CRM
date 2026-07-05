/**
 * Overall Inventory Product Search Widget
 * 9x1 type-ahead search with a six-tab Product Detail modal.
 *
 * Backend:
 * - VAS_078_ProductSearchWidget/SearchProducts
 * - VAS_078_ProductSearchWidget/GetProductDetail
 *
 * Summary Message Table
 *  # | Current Text                                  | Message Key
 * ---+-----------------------------------------------+-----------------------------
 *  1 | Search product by name, code, type or category| VAS_ProductSearchPlaceholder
 *  2 | Select a result for full product detail       | VAS_ProductSearchHelper
 *  3 | Searching...                                  | VAS_ProductSearching
 *  4 | No products match                             | VAS_ProductNoMatches
 *  5 | Product Detail                                | VAS_ProductDetail
 *  6 | Unable to load product detail.                | VAS_ProductDetailLoadError
 *  7 | Showing latest 5 records.                     | VAS_ShowingLatest5Records
 *  8 | No records found for this product.            | VAS_NoProductRecords
 *  9 | On Hand Qty                                   | VAS_OnHandQty
 * 10 | Stock Value                                   | VAS_StockValue
 * 11 | Reorder Point                                 | VAS_ReorderPoint
 * 12 | Overview                                      | VAS_Overview
 * 13 | Stock                                         | VAS_Stock
 * 14 | Purchase Orders                               | VAS_PurchaseOrders
 * 15 | Sales Orders                                  | VAS_SalesOrders
 * 16 | Movements                                     | VAS_Movements
 * 17 | Requisitions                                  | VAS_Requisitions
 * 18 | Preferred Supplier                            | VAS_PreferredSupplier
 * 19 | Product Code                                  | VAS_ProductCode
 * 20 | Product Type                                  | VAS_ProductType
 * 21 | Product Category                              | VAS_ProductCategory
 * 22 | Unit of Measure                               | VAS_UnitOfMeasure
 * 23 | Ordered Qty                                   | VAS_OrderedQty
 * 24 | Delivered Qty                                 | VAS_DeliveredQty
 * 25 | Required Date                                 | VAS_RequiredDate
 * 26 | Document Date                                 | VAS_DocumentDate
 * 27 | Previous page                                 | VAS_PreviousPage
 * 28 | Next page                                     | VAS_NextPage
 * 29 | Showing                                       | VAS_Showing
 * 30 | of                                            | VAS_Of
 * 31 | Discontinued From                             | VAS_DiscontinuedFrom
 * 32 | Attribute                                     | VAS_Attribute
 * 33 | Delivered                                     | VAS_StatusDelivered
 * 34 | Partial                                       | VAS_StatusPartial
 * 35 | Discontinued                                  | VAS_StatusDiscontinued
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_078_ProductSearchWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-product-search-root">');
        var $input;
        var $suggest;
        var $dialog;
        var $dialogTitle;
        var $dialogBadge;
        var $dialogBody;
        var $dashboardScroll;

        var searchTimer = null;
        var requestSequence = 0;
        var suggestions = [];
        var suggestionIndex = -1;
        var productDetail = null;
        var activeTab = 'overview';
        var searchCurrency = { symbol: '', iso: '', precision: 0 };
        var tabPages = {};
        var tablePageSize = 5;
        var openProductDetail;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function escapeHtml(value) {
            if (value == null) { return ''; }
            return String(value).replace(/[&<>"']/g, function (character) {
                return {
                    '&': '&amp;',
                    '<': '&lt;',
                    '>': '&gt;',
                    '"': '&quot;',
                    "'": '&#39;'
                }[character];
            });
        }

        function icon(name) {
            if (name === 'search') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="8"></circle><path d="m21 21-4.35-4.35"></path></svg>';
            }
            if (name === 'close') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>';
            }
            if (name === 'clock') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 2"></path></svg>';
            }
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m21 8-9 5-9-5"></path><path d="m3 8 9-5 9 5v8l-9 5-9-5Z"></path><path d="M12 13v8"></path></svg>';
        }

        function parseResponse(response) {
            var parsed = response;
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            return parsed || {};
        }

        // Currencies whose countries use the Indian numbering system (ISO 4217) -
        // these get Indian digit grouping and Lakh/Crore compact notation; every
        // other currency gets international grouping and K/M/B compact notation.
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function usesIndianNumbering(isoCode) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(isoCode || '').toUpperCase()) >= 0;
        }

        function currencyLocale(isoCode) {
            return usesIndianNumbering(isoCode) ? 'en-IN' : 'en-US';
        }

        function detailIso() {
            return (productDetail && productDetail.CurrencyIso) || searchCurrency.iso || '';
        }

        function formatQty(value) {
            var number = Number(value || 0);
            return number.toLocaleString(currencyLocale(detailIso()), {
                maximumFractionDigits: 2
            });
        }

        function trimTrailingZeros(text) {
            return text.replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1');
        }

        function formatCompactNumber(value, isoCode) {
            var number = Number(value || 0);
            var abs = Math.abs(number);
            if (usesIndianNumbering(isoCode)) {
                if (abs >= 10000000) { return trimTrailingZeros((number / 10000000).toFixed(2)) + ' Cr'; }
                if (abs >= 100000) { return trimTrailingZeros((number / 100000).toFixed(2)) + ' Lakh'; }
                if (abs >= 1000) { return trimTrailingZeros((number / 1000).toFixed(1)) + 'K'; }
            } else {
                if (abs >= 1000000000) { return trimTrailingZeros((number / 1000000000).toFixed(1)) + 'B'; }
                if (abs >= 1000000) { return trimTrailingZeros((number / 1000000).toFixed(1)) + 'M'; }
                if (abs >= 1000) { return trimTrailingZeros((number / 1000).toFixed(1)) + 'K'; }
            }
            return number.toLocaleString(currencyLocale(isoCode), { maximumFractionDigits: 2 });
        }

        function formatCompactQty(value) {
            return formatCompactNumber(value, detailIso());
        }

        function formatCompactAmount(value, symbol, isoCode) {
            var currency = symbol || isoCode || '';
            return currency + formatCompactNumber(value, isoCode);
        }

        function getPrecision(value) {
            var precision = Number(value);
            if (!isNaN(precision) && precision >= 0) { return precision; }
            if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                precision = Number(VIS.Env.getCtx().getStdPrecision());
            }
            return !isNaN(precision) && precision >= 0 ? precision : 0;
        }

        function formatAmount(value, symbol, isoCode, precision) {
            var number = Number(value || 0);
            var currency = symbol || isoCode || '';
            var stdPrecision = getPrecision(precision);
            var formatted = number.toLocaleString(currencyLocale(isoCode), {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            return currency ? currency + ' ' + formatted : formatted;
        }

        function formatBaseAmount(value) {
            return formatAmount(
                value,
                productDetail && productDetail.CurrencySymbol,
                productDetail && productDetail.CurrencyIso,
                productDetail && productDetail.StdPrecision
            );
        }

        function formatDate(value) {
            if (!value) { return '-'; }
            var date = new Date(value.replace(' ', 'T'));
            if (isNaN(date.getTime())) { return value; }
            return date.toLocaleDateString(window.navigator.language || 'en-IN', {
                year: 'numeric',
                month: 'short',
                day: '2-digit'
            });
        }

        function productTypeLabel(code) {
            var productTypes = {
                I: label('VAS_ProductTypeItem', 'Item'),
                S: label('VAS_ProductTypeService', 'Service'),
                R: label('VAS_ProductTypeResource', 'Resource'),
                E: label('VAS_ProductTypeExpense', 'Expense'),
                O: label('VAS_ProductTypeOnline', 'Online')
            };
            return productTypes[code] || code || '-';
        }

        function documentStatusLabel(code) {
            var statuses = {
                DR: label('VAS_StatusDraft', 'Draft'),
                IP: label('VAS_StatusInProgress', 'In Progress'),
                CO: label('VAS_StatusCompleted', 'Completed'),
                CL: label('VAS_StatusClosed', 'Closed'),
                AP: label('VAS_StatusApproved', 'Approved'),
                RE: label('VAS_StatusReversed', 'Reversed'),
                VO: label('VAS_StatusVoided', 'Voided')
            };
            return statuses[code] || code || '-';
        }

        // Review #5: a purchase order that ran (Completed/Closed) reports its delivery
        // progress - Delivered when everything arrived, Partial when some quantity
        // arrived, Completed while no GRN is made. Other documents keep their status.
        function purchaseOrderStatusLabel(order) {
            var code = order.DocumentStatus;
            if (code !== 'CO' && code !== 'CL') { return documentStatusLabel(code); }
            var ordered = Number(order.QuantityOrdered || 0);
            var delivered = Number(order.QuantityDelivered || 0);
            if (ordered > 0 && delivered >= ordered) { return label('VAS_StatusDelivered', 'Delivered'); }
            if (delivered > 0) { return label('VAS_StatusPartial', 'Partial'); }
            return label('VAS_StatusCompleted', 'Completed');
        }

        // Review #6: the status reads Active for every product unless the product
        // is flagged Discontinued (controller sends Status 'D').
        function productStatusLabel() {
            return productDetail.Status === 'D'
                ? label('VAS_StatusDiscontinued', 'Discontinued')
                : label('Active', 'Active');
        }

        function movementTypeLabel(code) {
            var movementTypes = {
                'V+': label('VAS_MovementReceipt', 'Receipt'),
                'V-': label('VAS_MovementVendorReturn', 'Vendor Return'),
                'C+': label('VAS_MovementCustomerReturn', 'Customer Return'),
                'C-': label('VAS_MovementShipment', 'Shipment / Issue'),
                'M+': label('VAS_MovementIn', 'Movement In'),
                'M-': label('VAS_MovementOut', 'Movement Out'),
                'I+': label('VAS_InventoryIncrease', 'Inventory Increase'),
                'I-': label('VAS_InventoryDecrease', 'Inventory Decrease'),
                'P+': label('VAS_ProductionIn', 'Production In'),
                'P-': label('VAS_ProductionOut', 'Production Out')
            };
            return movementTypes[code] || code || '-';
        }

        this.Initalize = function () {
            createWidget();
            createSuggestionList();
            createDialog();
            bindEvents();
            openProductDetail = function (productId, productName, productCode) {
                loadProductDetail(productId, productName, productCode);
            };
            VAS.openOverallInventoryProductDetail = openProductDetail;
        };

        function createWidget() {
            var placeholder = label('VAS_ProductSearchPlaceholder', 'Search product by name, code, type or category\u2026');
            var helper = label('VAS_ProductSearchHelper', 'Select a result for full product detail');

            $root.html(
                '<div class="MPC-product-search-pill">' +
                    '<span class="MPC-product-search-icon">' + icon('search') + '</span>' +
                    '<span class="MPC-product-search-input-wrap">' +
                        '<input class="MPC-product-search-input" type="text" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="MPC-product-search-suggestions-' + escapeHtml($self.windowNo || '') + '" placeholder="' + escapeHtml(placeholder) + '">' +
                    '</span>' +
                    '<span class="MPC-product-search-helper">' + escapeHtml(helper) + '</span>' +
                '</div>'
            );

            $input = $root.find('.MPC-product-search-input');
        }

        function createSuggestionList() {
            // The dropdown lives on <body> so the dashboard cell's overflow/stacking
            // cannot clip it or paint sibling widgets above it; it is positioned as a
            // fixed popover anchored to the search pill (matching the pill's width).
            $suggest = $('<div class="MPC-product-search-suggest" id="MPC-product-search-suggestions-' + escapeHtml($self.windowNo || '') + '" role="listbox">');
            $('body').append($suggest);
        }

        function positionSuggest() {
            if (!$suggest) { return; }
            var pill = $root.find('.MPC-product-search-pill')[0];
            if (!pill) { return; }
            var rect = pill.getBoundingClientRect();
            $suggest.css({
                left: Math.round(rect.left) + 'px',
                top: Math.round(rect.bottom + 6) + 'px',
                width: Math.round(rect.width) + 'px'
            });
        }

        function createDialog() {
            $dialog = $(
                '<div class="MPC-product-search-dialog-wrap" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="MPC-product-search-dialog-scrim"></div>' +
                    '<section class="MPC-product-search-dialog">' +
                        '<header class="MPC-product-search-dialog-titlebar">' +
                            '<div class="MPC-product-search-dialog-title-left">' +
                                '<h2></h2>' +
                                '<span class="MPC-product-search-dialog-badge"></span>' +
                            '</div>' +
                            '<button type="button" class="MPC-product-search-dialog-close" aria-label="' + escapeHtml(label('Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="MPC-product-search-dialog-body"></div>' +
                    '</section>' +
                '</div>'
            );

            $('body').append($dialog);
            $dialogTitle = $dialog.find('h2');
            $dialogBadge = $dialog.find('.MPC-product-search-dialog-badge');
            $dialogBody = $dialog.find('.MPC-product-search-dialog-body');
        }

        function bindEvents() {
            var eventNamespace = '.MPCProductSearch-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $input.on('input', scheduleSearch);
            $input.on('focus', function () {
                if ($input.val().trim()) { scheduleSearch(); }
            });
            $input.on('keydown', handleInputKeydown);

            $suggest.on('mousedown', '.MPC-product-search-option', function (event) {
                event.preventDefault();
                selectSuggestion(Number($(this).attr('data-index')));
            });

            $dialog.on('click', '.MPC-product-search-dialog-close, .MPC-product-search-dialog-scrim', closeDialog);
            $dialog.on('click', '.MPC-product-search-tab', function () {
                activeTab = $(this).attr('data-tab');
                renderTab();
            });
            $dialog.on('click', '.MPC-product-search-table-page', function () {
                var direction = $(this).attr('data-direction');
                var page = tabPages[activeTab] || 1;
                tabPages[activeTab] = page + (direction === 'next' ? 1 : -1);
                renderTab();
            });

            $(document).on('mousedown' + eventNamespace, function (event) {
                if (!$(event.target).closest('.MPC-product-search-pill, .MPC-product-search-suggest').length) {
                    closeSuggestions();
                }
            });
            $(document).on('keydown' + eventNamespace, function (event) {
                if (event.key !== 'Escape') { return; }
                if ($dialog.hasClass('is-open')) { closeDialog(); }
                else { closeSuggestions(); }
            });

            $(window).on('scroll' + eventNamespace, closeSuggestions);
            $(window).on('resize' + eventNamespace, function () {
                if ($suggest && $suggest.hasClass('is-open')) { positionSuggest(); }
            });

            $dashboardScroll = $root.closest('.vis-widget-container, [data-dashboard-container]');
            if ($dashboardScroll.length) {
                $dashboardScroll.on('scroll' + eventNamespace, closeSuggestions);
            }
        }

        function scheduleSearch() {
            if (searchTimer) { clearTimeout(searchTimer); }

            var searchText = $input.val().trim();
            if (!searchText) {
                requestSequence += 1;
                closeSuggestions();
                return;
            }

            searchTimer = setTimeout(function () {
                searchProducts(searchText);
            }, 250);
        }

        function searchProducts(searchText) {
            var sequence = ++requestSequence;
            renderSuggestionState(label('VAS_ProductSearching', 'Searching\u2026'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_078_ProductSearchWidget/SearchProducts',
                type: 'GET',
                dataType: 'json',
                data: {
                    q: searchText,
                    max: 7
                },
                success: function (response) {
                    if (sequence !== requestSequence) { return; }

                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        renderSuggestionState(parsed.Error);
                        return;
                    }

                    searchCurrency.symbol = parsed.CurrencySymbol || '';
                    searchCurrency.iso = parsed.CurrencyIso || '';
                    searchCurrency.precision = parsed.StdPrecision;
                    suggestions = parsed.Rows || [];
                    suggestionIndex = suggestions.length ? 0 : -1;
                    renderSuggestions(searchText);
                },
                error: function () {
                    if (sequence !== requestSequence) { return; }
                    renderSuggestionState(label('VAS_ProductDetailLoadError', 'Unable to load products.'));
                }
            });
        }

        function renderSuggestions(searchText) {
            if (!suggestions.length) {
                renderSuggestionState(label('VAS_ProductNoMatches', 'No products match') + ' "' + searchText + '".');
                return;
            }

            var html = suggestions.map(function (product, index) {
                var stockValue = formatAmount(
                    product.StockValue,
                    searchCurrency.symbol,
                    searchCurrency.iso,
                    searchCurrency.precision
                );
                var metadata = [
                    product.ProductCode,
                    productTypeLabel(product.ProductType),
                    product.CategoryName
                ].filter(function (value) { return value; }).join(' \u00b7 ');

                return '<div class="MPC-product-search-option' + (index === suggestionIndex ? ' is-active' : '') + '" role="option" aria-selected="' + (index === suggestionIndex ? 'true' : 'false') + '" data-index="' + index + '">' +
                    '<span class="MPC-product-search-option-icon">' + icon('product') + '</span>' +
                    '<span class="MPC-product-search-option-main">' +
                        '<span class="MPC-product-search-option-name" title="' + escapeHtml(product.ProductName) + '">' + escapeHtml(product.ProductName) + '</span>' +
                        '<span class="MPC-product-search-option-meta" title="' + escapeHtml(metadata) + '">' + escapeHtml(metadata) + '</span>' +
                    '</span>' +
                    '<span class="MPC-product-search-option-value" title="' + escapeHtml(stockValue) + '">' + escapeHtml(stockValue) + '</span>' +
                '</div>';
            }).join('');

            $suggest.html(html).addClass('is-open');
            positionSuggest();
            $input.attr('aria-expanded', 'true');
        }

        function renderSuggestionState(message) {
            suggestions = [];
            suggestionIndex = -1;
            $suggest.html('<div class="MPC-product-search-suggest-state">' + escapeHtml(message) + '</div>').addClass('is-open');
            positionSuggest();
            $input.attr('aria-expanded', 'true');
        }

        function closeSuggestions() {
            if (!$suggest) { return; }
            $suggest.removeClass('is-open').empty();
            $input.attr('aria-expanded', 'false');
            suggestionIndex = -1;
        }

        function handleInputKeydown(event) {
            if (event.key === 'Escape') {
                closeSuggestions();
                return;
            }

            if (!$suggest.hasClass('is-open') || !suggestions.length) { return; }

            if (event.key === 'ArrowDown') {
                event.preventDefault();
                suggestionIndex = (suggestionIndex + 1) % suggestions.length;
                renderSuggestions($input.val().trim());
            }
            else if (event.key === 'ArrowUp') {
                event.preventDefault();
                suggestionIndex = suggestionIndex <= 0 ? suggestions.length - 1 : suggestionIndex - 1;
                renderSuggestions($input.val().trim());
            }
            else if (event.key === 'Enter' && suggestionIndex >= 0) {
                event.preventDefault();
                selectSuggestion(suggestionIndex);
            }
        }

        function selectSuggestion(index) {
            var product = suggestions[index];
            if (!product) { return; }

            $input.val(product.ProductName);
            closeSuggestions();
            loadProductDetail(product.ProductId, product.ProductName, product.ProductCode);
        }

        function loadProductDetail(productId, productName, productCode) {
            openLoadingDialog(productName, productCode);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_078_ProductSearchWidget/GetProductDetail',
                type: 'GET',
                dataType: 'json',
                data: {
                    M_Product_ID: productId
                },
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (!parsed || parsed.Error || !parsed.Overview) {
                        renderDialogError(parsed.Error || label('VAS_ProductDetailLoadError', 'Unable to load product detail.'));
                        return;
                    }

                    productDetail = parsed;
                    activeTab = 'overview';
                    tabPages = {};
                    renderProductDialog();
                },
                error: function () {
                    renderDialogError(label('VAS_ProductDetailLoadError', 'Unable to load product detail.'));
                }
            });
        }

        function openLoadingDialog(productName, productCode) {
            $dialogTitle.text(productName || label('VAS_ProductDetail', 'Product Detail'));
            $dialogBadge.text(productCode || '').toggle(!!productCode);
            $dialogBody.html('<div class="MPC-product-search-dialog-state">' + escapeHtml(label('Loading', 'Loading\u2026')) + '</div>');
            $dialog.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-product-search-modal-open');
        }

        function renderDialogError(message) {
            $dialogBody.html('<div class="MPC-product-search-dialog-state MPC-product-search-dialog-error">' + escapeHtml(message) + '</div>');
        }

        function renderProductDialog() {
            var overview = productDetail.Overview;
            var status = productStatusLabel();

            $dialogTitle.text(overview.ProductName);
            $dialogBadge.text(overview.ProductCode || '').toggle(!!overview.ProductCode);

            var chips = [
                overview.ProductCode,
                productTypeLabel(overview.ProductType),
                overview.CategoryName,
                overview.UomName ? label('VAS_UnitOfMeasure', 'UoM') + ' - ' + overview.UomName : ''
            ].filter(function (value) { return value; }).map(function (value) {
                return '<span class="MPC-product-search-chip">' + escapeHtml(value) + '</span>';
            }).join('');

            var imageUrl = overview.ImageUrl;
            if (imageUrl && imageUrl.indexOf('http') !== 0 && imageUrl.indexOf('data:') !== 0) {
                var contextUrl = VIS.Application.contextUrl || '';
                if (contextUrl && contextUrl.lastIndexOf('/') !== contextUrl.length - 1 && imageUrl.indexOf('/') !== 0) {
                    imageUrl = contextUrl + '/' + imageUrl;
                } else if (contextUrl && contextUrl.lastIndexOf('/') === contextUrl.length - 1 && imageUrl.indexOf('/') === 0) {
                    imageUrl = contextUrl + imageUrl.substring(1);
                } else {
                    imageUrl = contextUrl + imageUrl;
                }
            }

            var heroIconContent = imageUrl
                ? '<img class="MPC-product-search-hero-img" src="' + escapeHtml(imageUrl) + '" alt="">'
                : icon('product');

            var hero = '<section class="MPC-product-search-hero">' +
                '<span class="MPC-product-search-hero-icon' + (overview.ImageUrl ? ' has-image' : '') + '">' + heroIconContent + '</span>' +
                '<div class="MPC-product-search-hero-main">' +
                    '<div class="MPC-product-search-hero-name">' + escapeHtml(overview.ProductName) + '</div>' +
                    '<div class="MPC-product-search-chips">' + chips + '</div>' +
                '</div>' +
                '<div class="MPC-product-search-stats">' +
                    statTile(label('VAS_OnHandQty', 'On Hand Qty'), formatCompactQty(productDetail.OnHandQty), '') +
                    statTile(label('VAS_StockValue', 'Stock Value'), formatCompactAmount(productDetail.StockValue, productDetail.CurrencySymbol, productDetail.CurrencyIso), '') +
                    statTile(label('VAS_ReorderPoint', 'Reorder Pt'), formatQty(productDetail.ReorderPoint), 'is-warning') +
                    statTile(label('Status', 'Status'), status, productDetail.Status === 'D' ? 'is-warning' : 'is-success') +
                '</div>' +
            '</section>';

            var tabs = [
                ['overview', label('VAS_Overview', 'Overview')],
                ['stock', label('VAS_Stock', 'Stock')],
                ['purchaseOrders', label('VAS_PurchaseOrders', 'Purchase Orders')],
                ['salesOrders', label('VAS_SalesOrders', 'Sales Orders')],
                ['movements', label('VAS_Movements', 'Movements')],
                ['requisitions', label('VAS_Requisitions', 'Requisitions')]
            ].map(function (tab) {
                return '<button type="button" class="MPC-product-search-tab' + (activeTab === tab[0] ? ' is-active' : '') + '" data-tab="' + tab[0] + '">' + escapeHtml(tab[1]) + '</button>';
            }).join('');

            $dialogBody.html(hero + '<nav class="MPC-product-search-tabs">' + tabs + '</nav><div class="MPC-product-search-tab-body"></div>');

            // A hero image that fails to load (file removed from the server, dead URL)
            // degrades to the product icon instead of a broken-image glyph.
            $dialogBody.find('.MPC-product-search-hero-img').on('error', function () {
                $(this).closest('.MPC-product-search-hero-icon').removeClass('has-image').html(icon('product'));
            });

            renderTab();
        }

        function statTile(title, value, className) {
            return '<div class="MPC-product-search-stat">' +
                '<div class="MPC-product-search-stat-label">' + escapeHtml(title) + '</div>' +
                '<div class="MPC-product-search-stat-value ' + className + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</div>' +
            '</div>';
        }

        function renderTab() {
            if (!productDetail) { return; }

            $dialog.find('.MPC-product-search-tab').removeClass('is-active')
                .filter('[data-tab="' + activeTab + '"]').addClass('is-active');

            var $tabBody = $dialog.find('.MPC-product-search-tab-body');
            if (activeTab === 'overview') {
                $tabBody.html(renderOverview());
            }
            else if (activeTab === 'stock') {
                $tabBody.html(renderStock());
            }
            else if (activeTab === 'purchaseOrders') {
                $tabBody.html(renderOrders(productDetail.PurchaseOrders || [], false));
            }
            else if (activeTab === 'salesOrders') {
                $tabBody.html(renderOrders(productDetail.SalesOrders || [], true));
            }
            else if (activeTab === 'movements') {
                $tabBody.html(renderMovements());
            }
            else if (activeTab === 'requisitions') {
                $tabBody.html(renderRequisitions());
            }
        }

        function renderOverview() {
            var overview = productDetail.Overview;
            var fields = [
                [label('VAS_ProductCode', 'Product Code'), overview.ProductCode],
                [label('Name', 'Name'), overview.ProductName],
                [label('UPC', 'UPC'), overview.UPC],
                [label('VAS_ProductType', 'Type'), productTypeLabel(overview.ProductType)],
                [label('VAS_ProductCategory', 'Product Category'), overview.CategoryName],
                [label('VAS_UnitOfMeasure', 'Unit of Measure'), overview.UomName],
                [label('VAS_PreferredSupplier', 'Preferred Supplier'), productDetail.PreferredSupplier],
                [label('VAS_OnHandQty', 'On Hand Qty'), formatQty(productDetail.OnHandQty), true],
                [label('VAS_StockValue', 'Stock Value'), formatBaseAmount(productDetail.StockValue), true],
                [label('VAS_ReorderPoint', 'Reorder Point'), formatQty(productDetail.ReorderPoint)],
                [label('Status', 'Status'), productStatusLabel()],
                [label('VAS_DiscontinuedFrom', 'Discontinued From'), formatDate(overview.DiscontinuedFrom)]
            ];

            return '<div class="MPC-product-search-form-grid">' + fields.map(function (field) {
                var value = field[1] || '-';
                return '<div class="MPC-product-search-field">' +
                    '<div class="MPC-product-search-field-label">' + escapeHtml(field[0]) + '</div>' +
                    '<div class="MPC-product-search-field-value' + (field[2] ? ' is-strong' : '') + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</div>' +
                '</div>';
            }).join('') + '</div>';
        }

        function renderStock() {
            var rows = (productDetail.Stock || []).map(function (stock) {
                return [
                    stock.WarehouseName,
                    stock.LocatorValue,
                    stock.Attribute,
                    formatQty(stock.Quantity),
                    formatBaseAmount(stock.StockValue)
                ];
            });

            return renderTable(
                [label('Warehouse', 'Warehouse'), label('Locator', 'Locator'), label('VAS_Attribute', 'Attribute'), label('VAS_OnHandQty', 'On Hand Qty'), label('Amount', 'Amount')],
                rows,
                [3, 4],
                'stock'
            );
        }

        function renderOrders(orders, isSales) {
            var headers = [
                isSales ? label('SalesOrder', 'SO #') : label('PurchaseOrder', 'PO #'),
                label('DateOrdered', 'Ordered'),
                label('DatePromised', 'Promised'),
                label('VAS_Attribute', 'Attribute'),
                label('VAS_OrderedQty', 'Ordered Qty'),
                label('VAS_DeliveredQty', 'Delivered Qty'),
                label('Amount', 'Amount'),
                label('Status', 'Status')
            ];

            var rows = orders.map(function (order) {
                return [
                    order.DocumentNo,
                    formatDate(order.DateOrdered),
                    formatDate(order.DatePromised),
                    order.Attribute,
                    formatQty(order.QuantityOrdered),
                    formatQty(order.QuantityDelivered),
                    formatAmount(order.LineNetAmount, order.CurrencySymbol, order.CurrencyIso, order.StdPrecision),
                    isSales ? documentStatusLabel(order.DocumentStatus) : purchaseOrderStatusLabel(order)
                ];
            });

            return latestRecordsNote() + renderTable(headers, rows, [4, 5, 6], isSales ? 'salesOrders' : 'purchaseOrders');
        }

        function renderMovements() {
            var rows = (productDetail.Movements || []).map(function (movement) {
                return [
                    formatDate(movement.MovementDate),
                    movementTypeLabel(movement.MovementType),
                    movement.Attribute,
                    (Number(movement.MovementQuantity) > 0 ? '+' : '') + formatQty(movement.MovementQuantity),
                    movement.WarehouseName,
                    movement.LocatorValue
                ];
            });

            return latestRecordsNote() + renderTable(
                [label('MovementDate', 'Date'), label('MovementType', 'Type'), label('VAS_Attribute', 'Attribute'), label('Qty', 'Qty'), label('Warehouse', 'Warehouse'), label('Locator', 'Locator')],
                rows,
                [3],
                'movements'
            );
        }

        function renderRequisitions() {
            var rows = (productDetail.Requisitions || []).map(function (requisition) {
                return [
                    requisition.DocumentNo,
                    formatDate(requisition.DocumentDate),
                    formatDate(requisition.RequiredDate),
                    requisition.Attribute,
                    formatQty(requisition.Quantity),
                    formatQty(requisition.QuantityOrdered),
                    documentStatusLabel(requisition.DocumentStatus)
                ];
            });

            return latestRecordsNote() + renderTable(
                [label('Requisition', 'Requisition #'), label('VAS_DocumentDate', 'Document Date'), label('VAS_RequiredDate', 'Required Date'), label('VAS_Attribute', 'Attribute'), label('Qty', 'Qty'), label('VAS_OrderedQty', 'Ordered Qty'), label('Status', 'Status')],
                rows,
                [4, 5],
                'requisitions'
            );
        }

        function latestRecordsNote() {
            return '<div class="MPC-product-search-note">' + icon('clock') + '<span>' + escapeHtml(label('VAS_ShowingLatest5Records', 'Showing latest 5 records.')) + '</span></div>';
        }

        function renderTable(headers, rows, rightAlignedColumns, pageKey) {
            if (!rows.length) {
                return '<div class="MPC-product-search-empty">' + escapeHtml(label('VAS_NoProductRecords', 'No records found for this product.')) + '</div>';
            }

            var totalPages = Math.max(1, Math.ceil(rows.length / tablePageSize));
            var page = Math.min(Math.max(tabPages[pageKey] || 1, 1), totalPages);
            tabPages[pageKey] = page;
            var start = (page - 1) * tablePageSize;
            var pageRows = rows.slice(start, start + tablePageSize);

            var head = headers.map(function (header, index) {
                return '<th class="' + (rightAlignedColumns.indexOf(index) >= 0 ? 'is-right' : '') + '" title="' + escapeHtml(header) + '">' + escapeHtml(header) + '</th>';
            }).join('');

            var body = pageRows.map(function (row) {
                return '<tr>' + row.map(function (cell, index) {
                    var value = cell == null || cell === '' ? '-' : cell;
                    return '<td class="' + (index === 0 ? 'is-primary ' : '') + (rightAlignedColumns.indexOf(index) >= 0 ? 'is-right' : '') + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</td>';
                }).join('') + '</tr>';
            }).join('');

            var pager = '';
            if (totalPages > 1) {
                pager = '<div class="MPC-product-search-table-footer">' +
                    '<span>' + escapeHtml(label('VAS_Showing', 'Showing')) + ' ' + (start + 1) + '\u2013' + (start + pageRows.length) + ' ' + escapeHtml(label('VAS_Of', 'of')) + ' ' + rows.length + '</span>' +
                    '<span class="MPC-product-search-table-pager">' +
                        '<button type="button" class="MPC-product-search-table-page" data-direction="previous" aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '"' + (page === 1 ? ' disabled' : '') + '>&lsaquo;</button>' +
                        '<span>' + page + ' ' + escapeHtml(label('VAS_Of', 'of')) + ' ' + totalPages + '</span>' +
                        '<button type="button" class="MPC-product-search-table-page" data-direction="next" aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '"' + (page === totalPages ? ' disabled' : '') + '>&rsaquo;</button>' +
                    '</span>' +
                '</div>';
            }

            return '<div class="MPC-product-search-table-wrap"><table class="MPC-product-search-table"><thead><tr>' + head + '</tr></thead><tbody>' + body + '</tbody></table></div>' + pager;
        }

        function closeDialog() {
            if (!$dialog) { return; }
            if (document.activeElement && $dialog[0].contains(document.activeElement)) {
                if ($input && $input.length) { $input.focus(); } else { document.activeElement.blur(); }
            }
            $dialog.removeClass('is-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-product-search-modal-open');
            productDetail = null;
        }

        this.refreshWidget = function () {
            requestSequence += 1;
            suggestions = [];
            productDetail = null;
            if (searchTimer) { clearTimeout(searchTimer); }
            if ($input) { $input.val(''); }
            closeSuggestions();
            closeDialog();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            var eventNamespace = '.MPCProductSearch-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            if (searchTimer) { clearTimeout(searchTimer); }
            $(document).off(eventNamespace);
            $(window).off(eventNamespace);
            if ($dashboardScroll && $dashboardScroll.length) { $dashboardScroll.off(eventNamespace); }
            if ($suggest) { $suggest.remove(); $suggest = null; }
            if ($dialog) { $dialog.remove(); $dialog = null; }
            if (VAS.openOverallInventoryProductDetail === openProductDetail) {
                delete VAS.openOverallInventoryProductDetail;
            }
            $('body').removeClass('MPC-product-search-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_078_ProductSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_078_ProductSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_078_ProductSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_078_ProductSearchWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_078_ProductSearchWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_078_ProductSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
