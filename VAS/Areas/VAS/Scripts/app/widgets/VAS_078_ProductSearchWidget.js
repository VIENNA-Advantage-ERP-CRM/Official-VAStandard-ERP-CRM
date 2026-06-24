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
 *  9 | On Hand                                       | VAS_OnHand
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

        function formatQty(value) {
            var number = Number(value || 0);
            return number.toLocaleString(window.navigator.language || 'en-IN', {
                maximumFractionDigits: 2
            });
        }

        function formatCompactQty(value) {
            var number = Number(value || 0);
            if (Math.abs(number) >= 10000000) { return (number / 10000000).toFixed(2).replace(/\.00$/, '') + 'Cr'; }
            if (Math.abs(number) >= 100000) { return (number / 100000).toFixed(2).replace(/\.00$/, '') + 'L'; }
            if (Math.abs(number) >= 1000) { return (number / 1000).toFixed(1).replace(/\.0$/, '') + 'K'; }
            return formatQty(number);
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
            var formatted = number.toLocaleString(window.navigator.language, {
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
            $suggest = $('<div class="MPC-product-search-suggest" id="MPC-product-search-suggestions-' + escapeHtml($self.windowNo || '') + '" role="listbox">');
            $root.find('.MPC-product-search-input-wrap').append($suggest);
        }

        function createDialog() {
            $dialog = $(
                '<div class="modal-wrap" id="modalWrap-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '">' +
                    '<div class="modal-scrim" id="modalScrim-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '"></div>' +
                    '<div class="modal">' +
                        '<div class="modal-title">' +
                            '<div class="mt-left">' +
                                '<h3 id="modalTitle-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '">Detail</h3>' +
                                '<span class="mt-badge" id="modalBadge-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '" style="display:none;"></span>' +
                            '</div>' +
                            '<button class="modal-close" id="modalClose-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="6" x2="18" y2="18"></line><line x1="18" y1="6" x2="6" y2="18"></line></svg>' +
                            '</button>' +
                        '</div>' +
                        '<div class="modal-body" id="modalBody-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '"></div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($dialog);
            $dialogTitle = $dialog.find('h3');
            $dialogBadge = $dialog.find('.mt-badge');
            $dialogClose = $dialog.find('.modal-close');
            $dialogBody = $dialog.find('.modal-body');
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
            $dialog.on('click', '.seg button', function () {
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
                    product.SKU,
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
            $input.attr('aria-expanded', 'true');
        }

        function renderSuggestionState(message) {
            suggestions = [];
            suggestionIndex = -1;
            $suggest.html('<div class="MPC-product-search-suggest-state">' + escapeHtml(message) + '</div>').addClass('is-open');
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
            if (productCode) {
                $dialogBadge.html('<span class="pill info">' + escapeHtml(productCode) + '</span>').show();
            } else {
                $dialogBadge.hide();
            }
            $dialogBody.html('<div style="padding: 24px; text-align: center; color: #748494;">' + escapeHtml(label('Loading', 'Loading\u2026')) + '</div>');
            $dialog.addClass('open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-product-search-modal-open');
        }

        function renderDialogError(message) {
            $dialogBody.html('<div style="padding: 24px; text-align: center; color: #A33F3F;">' + escapeHtml(message) + '</div>');
        }

        function renderProductDialog() {
            var overview = productDetail.Overview;
            var statusStr = productDetail.Status === 'Y' ? label('Active', 'Active') : label('Inactive', 'Inactive');
            var statusCls = productDetail.Status === 'Y' ? 'ok' : 'warn';

            $dialogTitle.text(overview.ProductName);
            if (overview.ProductCode) {
                $dialogBadge.html('<span class="pill info">' + escapeHtml(overview.ProductCode) + '</span>').show();
            } else {
                $dialogBadge.hide();
            }

            var chips = [
                overview.ProductCode,
                productTypeLabel(overview.ProductType),
                overview.CategoryName,
                overview.UomName ? label('VAS_UnitOfMeasure', 'UoM') + ' - ' + overview.UomName : ''
            ].filter(function (value) { return value; }).map(function (value) {
                return '<span class="pchip">' + escapeHtml(value) + '</span>';
            }).join('');

            var hero = '<div class="phero">' +
                '<div class="phero-ico">' + icon('box') + '</div>' +
                '<div class="phero-main">' +
                    '<div class="phero-name">' + escapeHtml(overview.ProductName) + '</div>' +
                    '<div class="phero-chips">' + chips + '</div>' +
                '</div>' +
                '<div class="phero-stats">' +
                    statTile(label('VAS_OnHand', 'On Hand'), formatCompactQty(productDetail.OnHandQty), '') +
                    statTile(label('VAS_StockValue', 'Stock Value'), formatBaseAmount(productDetail.StockValue), '') +
                    statTile(label('VAS_ReorderPoint', 'Reorder Pt'), formatQty(productDetail.ReorderPoint), 'warn') +
                    statTile(label('Status', 'Status'), statusStr, statusCls) +
                '</div>' +
            '</div>';

            var tabs = [
                ['overview', label('VAS_Overview', 'Overview')],
                ['stock', label('VAS_Stock', 'Stock')],
                ['purchaseOrders', label('VAS_PurchaseOrders', 'Purchase Orders')],
                ['salesOrders', label('VAS_SalesOrders', 'Sales Orders')],
                ['movements', label('VAS_Movements', 'Movements')],
                ['requisitions', label('VAS_Requisitions', 'Requisitions')]
            ].map(function (tab) {
                return '<button type="button" class="' + (activeTab === tab[0] ? 'active' : '') + '" data-tab="' + tab[0] + '">' + escapeHtml(tab[1]) + '</button>';
            }).join('');

            $dialogBody.html(hero + '<div class="seg">' + tabs + '</div><div class="modal-tab-content"></div>');
            renderTab();
        }

        function statTile(title, value, className) {
            return '<div class="pstat">' +
                '<div class="l">' + escapeHtml(title) + '</div>' +
                '<div class="v ' + className + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</div>' +
            '</div>';
        }

        function renderTab() {
            if (!productDetail) { return; }

            $dialog.find('.seg button').removeClass('active')
                .filter('[data-tab="' + activeTab + '"]').addClass('active');

            var $tabBody = $dialog.find('.modal-tab-content');
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
                [label('SKU', 'SKU'), overview.SKU],
                [label('UPC', 'UPC'), overview.UPC],
                [label('VAS_ProductType', 'Type'), productTypeLabel(overview.ProductType)],
                [label('VAS_ProductCategory', 'Category'), overview.CategoryName],
                [label('VAS_UnitOfMeasure', 'Unit of Measure'), overview.UomName],
                [label('VAS_PreferredSupplier', 'Preferred Supplier'), productDetail.PreferredSupplier],
                [label('VAS_OnHand', 'On Hand'), formatQty(productDetail.OnHandQty), true],
                [label('VAS_StockValue', 'Stock Value'), formatBaseAmount(productDetail.StockValue), true],
                [label('VAS_ReorderPoint', 'Reorder Point'), formatQty(productDetail.ReorderPoint)],
                [label('Status', 'Status'), productDetail.Status === 'Y' ? label('Active', 'Active') : label('Inactive', 'Inactive')]
            ];

            return '<div class="form-grid">' + fields.map(function (field) {
                var value = field[1] || '-';
                return '<div class="field">' +
                    '<div class="field-label">' + escapeHtml(field[0]) + '</div>' +
                    '<div class="field-value' + (field[2] ? ' strong' : '') + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</div>' +
                '</div>';
            }).join('') + '</div>';
        }

        function renderStock() {
            var rows = (productDetail.Stock || []).map(function (stock) {
                return [
                    stock.WarehouseName,
                    stock.LocatorValue,
                    formatQty(stock.Quantity),
                    formatBaseAmount(stock.StockValue)
                ];
            });

            return renderTable(
                [label('Warehouse', 'Warehouse'), label('Locator', 'Locator'), label('Qty', 'Qty'), label('Value', 'Value')],
                rows,
                [2, 3],
                'stock'
            );
        }

        function renderOrders(orders, isSales) {
            var headers = [
                isSales ? label('SalesOrder', 'SO #') : label('PurchaseOrder', 'PO #'),
                label('DateOrdered', 'Ordered'),
                label('DatePromised', 'Promised'),
                label('VAS_OrderedQty', 'Ordered Qty'),
                label('VAS_DeliveredQty', 'Delivered Qty'),
                label('Value', 'Value'),
                label('Status', 'Status')
            ];

            var rows = orders.map(function (order) {
                return [
                    order.DocumentNo,
                    formatDate(order.DateOrdered),
                    formatDate(order.DatePromised),
                    formatQty(order.QuantityOrdered),
                    formatQty(order.QuantityDelivered),
                    formatAmount(order.LineNetAmount, order.CurrencySymbol, order.CurrencyIso, order.StdPrecision),
                    documentStatusLabel(order.DocumentStatus)
                ];
            });

            return latestRecordsNote() + renderTable(headers, rows, [3, 4, 5], isSales ? 'salesOrders' : 'purchaseOrders');
        }

        function renderMovements() {
            var rows = (productDetail.Movements || []).map(function (movement) {
                return [
                    formatDate(movement.MovementDate),
                    movementTypeLabel(movement.MovementType),
                    (Number(movement.MovementQuantity) > 0 ? '+' : '') + formatQty(movement.MovementQuantity),
                    movement.WarehouseName,
                    movement.LocatorValue
                ];
            });

            return latestRecordsNote() + renderTable(
                [label('MovementDate', 'Date'), label('MovementType', 'Type'), label('Qty', 'Qty'), label('Warehouse', 'Warehouse'), label('Locator', 'Locator')],
                rows,
                [2],
                'movements'
            );
        }

        function renderRequisitions() {
            var rows = (productDetail.Requisitions || []).map(function (requisition) {
                return [
                    requisition.DocumentNo,
                    formatDate(requisition.DocumentDate),
                    formatDate(requisition.RequiredDate),
                    formatQty(requisition.Quantity),
                    formatQty(requisition.QuantityOrdered),
                    documentStatusLabel(requisition.DocumentStatus)
                ];
            });

            return latestRecordsNote() + renderTable(
                [label('Requisition', 'Requisition #'), label('VAS_DocumentDate', 'Document Date'), label('VAS_RequiredDate', 'Required Date'), label('Qty', 'Qty'), label('VAS_OrderedQty', 'Ordered Qty'), label('Status', 'Status')],
                rows,
                [3, 4],
                'requisitions'
            );
        }

        function latestRecordsNote() {
            return '<div class="note">' + icon('clock') + escapeHtml(label('VAS_ShowingLatest5Records', 'Showing latest 5 records (most recent first).')) + '</div>';
        }

        function renderTable(headers, rows, rightAlignedColumns, pageKey) {
            if (!rows || !rows.length) {
                return '<div style="padding:20px 4px; color: #748494; font-size: 0.875rem;">' + escapeHtml(label('VAS_NoProductRecords', 'No records on file for this item.')) + '</div>';
            }

            var displayRows = rows.slice(0, 5);

            var th = headers.map(function(h, i) {
                return '<th class="' + (rightAlignedColumns.indexOf(i) > -1 ? 'r' : '') + '">' + escapeHtml(h) + '</th>';
            }).join('');

            var tr = displayRows.map(function(row) {
                return '<tr>' + row.map(function(c, i) {
                    var value = c == null || c === '' ? '-' : c;
                    return '<td class="' + (i === 0 ? 's ' : '') + (rightAlignedColumns.indexOf(i) > -1 ? 'r' : '') + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</td>';
                }).join('') + '</tr>';
            }).join('');

            return '<table class="mini-table"><thead><tr>' + th + '</tr></thead><tbody>' + tr + '</tbody></table>';
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.removeClass('open').attr('aria-hidden', 'true');
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
