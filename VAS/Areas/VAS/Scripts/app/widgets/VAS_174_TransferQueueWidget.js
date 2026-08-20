/**
 * Transfer Queue Widget (Material Transfer Dashboard)
 * Purpose - 3x2 table-based work-queue of all non-completed (open) material
 *           transfers. Client-side filtering by month/year. Row click opens a
 *           detail modal with a read-only field grid and paginated transfer lines.
 * Prefix  - VAS_174_
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

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function escapeHtml(v) {
        return String(v == null ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    function monthNames() {
        return lbl('VAS_174_Months', 'Jan,Feb,Mar,Apr,May,Jun,Jul,Aug,Sep,Oct,Nov,Dec').split(',');
    }

    function formatDate(iso) {
        if (!iso) { return '—'; }
        var d = new Date(iso);
        if (isNaN(d)) { return iso; }
        return d.getDate() + ' ' + monthNames()[d.getMonth()] + ' ' + d.getFullYear();
    }

    var STATUS_CLASS_MAP = {
        'DR': 'vas-174-pill--draft',
        'IP': 'vas-174-pill--progress',
        'WC': 'vas-174-pill--progress',
        'DP': 'vas-174-pill--progress',
        'UC': 'vas-174-pill--progress',
        'RE': 'vas-174-pill--closed',
        'VO': 'vas-174-pill--closed',
        'IN': 'vas-174-pill--progress'
    };

    var STATUS_LABEL_KEY_MAP = {
        'DR': { key: 'VAS_174_StatusDrafted',    fallback: 'Drafted' },
        'IP': { key: 'VAS_174_StatusInProgress', fallback: 'In Progress' },
        'WC': { key: 'VAS_174_StatusInProgress', fallback: 'In Progress' },
        'DP': { key: 'VAS_174_StatusInProgress', fallback: 'In Progress' },
        'UC': { key: 'VAS_174_StatusInProgress', fallback: 'In Progress' },
        'RE': { key: 'VAS_174_StatusClosed',     fallback: 'Closed' },
        'VO': { key: 'VAS_174_StatusClosed',     fallback: 'Closed' },
        'IN': { key: 'VAS_174_StatusInProgress', fallback: 'In Progress' }
    };

    function pillHtml(docStatus) {
        var cls = STATUS_CLASS_MAP[docStatus] || 'vas-174-pill--draft';
        var labelInfo = STATUS_LABEL_KEY_MAP[docStatus];
        var txt = labelInfo ? lbl(labelInfo.key, labelInfo.fallback) : docStatus;
        return '<span class="vas-174-pill ' + cls + '">' + escapeHtml(txt) + '</span>';
    }

    function lineStatusPill(lineStatusStr) {
        if (lineStatusStr === 'OK')      { return '<span class="vas-174-pill vas-174-pill--ok">OK</span>'; }
        if (lineStatusStr === 'Short')   { return '<span class="vas-174-pill vas-174-pill--short">Short</span>'; }
        return '<span class="vas-174-pill vas-174-pill--pending">Pending</span>';
    }

// ===== NEW CODE START — currency format (agent C08, 2026-08-19) =====
    var INDIAN_ISOS = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

    function formatCurrency(val, currencyObj) {
        var c = currencyObj || (window.VAS_CurrencyInfo || { iso: 'USD', symbol: '$' });
        var symbol = c.symbol || '$';
        var iso = (c.iso || '').toUpperCase();
        var num = parseFloat(val);

        if (val === null || val === undefined || isNaN(num)) {
            num = 0;
        }

        var absVal = Math.abs(num);
        var isIndian = INDIAN_ISOS.indexOf(iso) !== -1;
        var compactStr = '';
        var exactStr = '';

        if (isIndian) {
            exactStr = num.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            if (absVal >= 10000000) {
                compactStr = (num / 10000000).toFixed(2) + ' Cr';
            } else if (absVal >= 100000) {
                compactStr = (num / 100000).toFixed(2) + ' L';
            } else if (absVal >= 1000) {
                compactStr = (num / 1000).toFixed(2) + ' K';
            } else {
                compactStr = exactStr;
            }
        } else {
            exactStr = num.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            if (absVal >= 1000000000) {
                compactStr = (num / 1000000000).toFixed(2) + ' B';
            } else if (absVal >= 1000000) {
                compactStr = (num / 1000000).toFixed(2) + ' M';
            } else if (absVal >= 1000) {
                compactStr = (num / 1000).toFixed(2) + ' K';
            } else {
                compactStr = exactStr;
            }
        }

        return {
            formatted: symbol + ' ' + compactStr,
            exact: symbol + ' ' + exactStr
        };
    }

    function formatCount(val) {
        var num = parseInt(val, 10);
        if (isNaN(num)) { num = 0; }
        return {
            formatted: num.toLocaleString(),
            exact: num.toString()
        };
    }
// ===== NEW CODE END — currency format =====

    VAS.VAS_174_TransferQueueWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-174-container">');
        var $root = $('<div class="vas-174-root">');
        var widgetObserver = null;

        var now = new Date();
        var selMonth = now.getMonth() + 1;
        var selYear = now.getFullYear();

        var allRecords = [];
        var filteredRecords = [];
        var PAGE_SIZE = 5;
        var currentPage = 1;

        var $modal = null;
        var activeDoc = null;
        var modalPage = 1;
        var MODAL_PAGE_SIZE = 8;

        var monthDdOpen = false;
        var yearDdOpen = false;

        function buildWidget() {
            $root.html(
                '<div class="vas-174-header-row">' +
                    '<div class="vas-174-left-cluster">' +
                        '<div class="vas-174-icon-well">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<polyline points="17 1 21 5 17 9"></polyline>' +
                                '<path d="M3 11V9a4 4 0 0 1 4-4h14"></path>' +
                                '<polyline points="7 23 3 19 7 15"></polyline>' +
                                '<path d="M21 13v2a4 4 0 0 1-4 4H3"></path>' +
                            '</svg>' +
                        '</div>' +
                        '<div class="vas-174-title-block">' +
                            '<div class="vas-174-title">' + escapeHtml(lbl('VAS_174_TransferQueue', 'Transfer Queue')) + '</div>' +
                            '<div class="vas-174-subtitle">' + escapeHtml(lbl('VAS_174_OpenDocuments', 'Open documents only')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<span class="vas-174-count-pill" id="vas174-cnt">0 open</span>' +
                    '<div class="vas-174-filters">' +
                        '<div class="vas-174-dropdown" id="vas174-m-dd">' +
                            '<button type="button" class="vas-174-dropdown-btn" id="vas174-m-btn">' +
                                '<span id="vas174-m-lbl">' + monthNames()[selMonth - 1] + '</span>' +
                            '</button>' +
                        '</div>' +
                        '<div class="vas-174-dropdown" id="vas174-y-dd">' +
                            '<button type="button" class="vas-174-dropdown-btn" id="vas174-y-btn">' +
                                '<span id="vas174-y-lbl">' + selYear + '</span>' +
                            '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>' +
                '<div class="vas-174-table-wrap">' +
                    '<div class="vas-174-table-header">' +
                        '<span class="vas-174-th">' + escapeHtml(lbl('VAS_174_Document', 'Document')) + '</span>' +
                        '<span class="vas-174-th">' + escapeHtml(lbl('VAS_174_Type', 'Type')) + '</span>' +
                        '<span class="vas-174-th">' + escapeHtml(lbl('VAS_174_FromTo', 'From → To')) + '</span>' +
                        '<span class="vas-174-th">' + escapeHtml(lbl('VAS_174_Date', 'Date')) + '</span>' +
                        '<span class="vas-174-th vas-174-th-right">' + escapeHtml(lbl('VAS_174_Items', 'Items')) + '</span>' +
                        '<span class="vas-174-th vas-174-th-center">' + escapeHtml(lbl('VAS_174_Status', 'Status')) + '</span>' +
                    '</div>' +
                    '<div class="vas-174-table-body"></div>' +
                '</div>' +
                '<div class="vas-174-footer"></div>'
            );

            $root.find('#vas174-m-btn').on('click', function (e) {
                e.stopPropagation();
                toggleMonthDropdown();
            });

            $root.find('#vas174-y-btn').on('click', function (e) {
                e.stopPropagation();
                toggleYearDropdown();
            });

            $(document).on('click.vas174', function () {
                closeDropdowns();
            });

            $wrapper.append($root);

            // Self-Sizing Observer — feeds --widget-inline-size, which the root
            // font-size clamp reads. Without it the CSS falls back to 380px and
            // the whole widget renders smaller than VAS_165 / VAS_161.
            if (window.ResizeObserver && $wrapper[0]) {
                widgetObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                widgetObserver.observe($wrapper[0]);
            }

        }

        function toggleMonthDropdown() {
            yearDdOpen = false;
            $root.find('#vas174-y-dd .vas-174-dropdown-menu').remove();
            monthDdOpen = !monthDdOpen;
            if (monthDdOpen) { renderMonthMenu(); } else { $root.find('#vas174-m-dd .vas-174-dropdown-menu').remove(); }
        }

        function toggleYearDropdown() {
            monthDdOpen = false;
            $root.find('#vas174-m-dd .vas-174-dropdown-menu').remove();
            yearDdOpen = !yearDdOpen;
            if (yearDdOpen) { renderYearMenu(); } else { $root.find('#vas174-y-dd .vas-174-dropdown-menu').remove(); }
        }

        function closeDropdowns() {
            monthDdOpen = false;
            yearDdOpen = false;
            $root.find('.vas-174-dropdown-menu').remove();
        }

        function renderMonthMenu() {
            var $dd = $root.find('#vas174-m-dd');
            $dd.find('.vas-174-dropdown-menu').remove();

            var months = monthNames();
            var html = '<div class="vas-174-dropdown-menu" onclick="event.stopPropagation()">';
            html += '<div class="vas-174-dd-option' + (selMonth === 0 ? ' vas-174-dd-option--sel' : '') + '" data-m="0">' + escapeHtml(lbl('VAS_174_AllMonths', 'All Months')) + '</div>';
            for (var m = 1; m <= 12; m++) {
                html += '<div class="vas-174-dd-option' + (selMonth === m ? ' vas-174-dd-option--sel' : '') + '" data-m="' + m + '">' + escapeHtml(months[m - 1]) + '</div>';
            }
            html += '</div>';

            var $menu = $(html);
            $dd.append($menu);

            $menu.find('.vas-174-dd-option').on('click', function () {
                selMonth = parseInt($(this).attr('data-m'), 10);
                $root.find('#vas174-m-lbl').text(selMonth === 0 ? lbl('VAS_174_AllMonths', 'All Months') : months[selMonth - 1]);
                closeDropdowns();
                applyFilters();
            });
        }

        function renderYearMenu() {
            var $dd = $root.find('#vas174-y-dd');
            $dd.find('.vas-174-dropdown-menu').remove();

            var years = [now.getFullYear(), now.getFullYear() - 1, now.getFullYear() - 2];
            var html = '<div class="vas-174-dropdown-menu" onclick="event.stopPropagation()">';
            html += '<div class="vas-174-dd-option' + (selYear === 0 ? ' vas-174-dd-option--sel' : '') + '" data-y="0">' + escapeHtml(lbl('VAS_174_AllYears', 'All Years')) + '</div>';
            for (var i = 0; i < years.length; i++) {
                var y = years[i];
                html += '<div class="vas-174-dd-option' + (selYear === y ? ' vas-174-dd-option--sel' : '') + '" data-y="' + y + '">' + y + '</div>';
            }
            html += '</div>';

            var $menu = $(html);
            $dd.append($menu);

            $menu.find('.vas-174-dd-option').on('click', function () {
                selYear = parseInt($(this).attr('data-y'), 10);
                $root.find('#vas174-y-lbl').text(selYear === 0 ? lbl('VAS_174_AllYears', 'All Years') : selYear);
                closeDropdowns();
                applyFilters();
            });
        }

        function applyFilters() {
            filteredRecords = allRecords.filter(function (r) {
                if (!r.MovementDate) { return true; }
                var d = new Date(r.MovementDate);
                if (isNaN(d)) { return true; }
                if (selMonth !== 0 && (d.getMonth() + 1) !== selMonth) { return false; }
                if (selYear !== 0 && d.getFullYear() !== selYear) { return false; }
                return true;
            });

            $root.find('#vas174-cnt').text(filteredRecords.length + ' ' + lbl('VAS_174_Open', 'open'));
            currentPage = 1;
            renderTable();
        }

        function renderTable() {
            var $body = $root.find('.vas-174-table-body');
            var $footer = $root.find('.vas-174-footer');
            var total = filteredRecords.length;
            var totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
            currentPage = Math.min(currentPage, totalPages);

            if (total === 0) {
                $body.html(
                    '<div class="vas-174-empty">' +
                        '<div class="vas-174-empty-title">' + escapeHtml(lbl('VAS_174_NoOpenDocuments', 'No open transfer documents for this period.')) + '</div>' +
                        '<div class="vas-174-empty-sub">' + escapeHtml(lbl('VAS_174_TryDifferent', 'Try a different month or year.')) + '</div>' +
                    '</div>'
                );
                renderFooter($footer, 0, 0, 0, 1, 1);
                return;
            }

            var start = (currentPage - 1) * PAGE_SIZE;
            var end = Math.min(start + PAGE_SIZE, total);
            var slice = filteredRecords.slice(start, end);

            var html = '';
            for (var i = 0; i < slice.length; i++) {
                var r = slice[i];
                var routeTxt = (r.FromWarehouse || '') + ' <span class="vas-174-arrow">→</span> ' + (r.ToWarehouse || '');
                html += '<button type="button" class="vas-174-row" data-idx="' + (start + i) + '">' +
                    '<span class="vas-174-docno" title="' + escapeHtml(r.DocumentNo) + '">' + escapeHtml(r.DocumentNo || '—') + '</span>' +
                    '<span class="vas-174-doctype" title="' + escapeHtml(r.DocTypeName) + '">' + escapeHtml(r.DocTypeName || '—') + '</span>' +
                    '<span class="vas-174-route" title="' + escapeHtml((r.FromWarehouse || '') + ' → ' + (r.ToWarehouse || '')) + '">' + routeTxt + '</span>' +
                    '<span class="vas-174-date">' + escapeHtml(formatDate(r.MovementDate)) + '</span>' +
                    '<span class="vas-174-items">' + (r.LineCount || 0) + '</span>' +
                    '<span class="vas-174-status-cell">' + pillHtml(r.DocStatus) + '</span>' +
                '</button>';
            }
            $body.html(html);

            $body.find('.vas-174-row').on('click', function () {
                var idx = parseInt($(this).attr('data-idx'), 10);
                openModal(filteredRecords[idx]);
            });

            renderFooter($footer, start + 1, end, total, currentPage, totalPages);
        }

        function renderFooter($footer, s, e, total, page, totalPages) {
            var showingTxt = total === 0 ? '' :
                lbl('VAS_174_Showing', 'Showing') + ' ' + s + '–' + e + ' ' + lbl('VAS_174_Of', 'of') + ' ' + total;
            $footer.html(
                '<span class="vas-174-footer-helper">' + escapeHtml(showingTxt) + '</span>' +
                '<span class="vas-174-pager">' +
                    '<button type="button" class="vas-174-pager-btn" id="vas174-prev"' + (page <= 1 ? ' disabled' : '') + '>&#8249;</button>' +
                    '<span class="vas-174-pager-label">' + page + ' ' + lbl('VAS_174_Of', 'of') + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-174-pager-btn" id="vas174-next"' + (page >= totalPages ? ' disabled' : '') + '>&#8250;</button>' +
                '</span>'
            );
            $footer.find('#vas174-prev').on('click', function () { if (currentPage > 1) { currentPage--; renderTable(); } });
            $footer.find('#vas174-next').on('click', function () { if (currentPage < totalPages) { currentPage++; renderTable(); } });
        }

        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_174_TransferQueueWidget/GetTransferQueue',
                type: 'GET',
                cache: false,
                success: function (res) {
// ===== NEW CODE START — currency format (agent C08, 2026-08-19) =====
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }
                    if (data && data.currency) {
                        window.VAS_CurrencyInfo = data.currency;
                    }
                    allRecords = (data && !data.error && Array.isArray(data.records)) ? data.records : [];
                    applyFilters();
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//                    var data = typeof res === 'string' ? JSON.parse(res) : res;
//                    if (data && typeof data === 'string') { data = JSON.parse(data); }
//                    allRecords = (data && !data.error && Array.isArray(data.records)) ? data.records : [];
//                    applyFilters();
// ----- END OLD CODE -----
                },
                error: function () {
                    allRecords = [];
                    applyFilters();
                }
            });
        }

        function openModal(doc) {
            if (!doc) { return; }
            activeDoc = doc;
            modalPage = 1;
            renderModal();
        }

        function renderModal() {
            var doc = activeDoc;
            var lines = doc.Lines || [];
            var totalLines = lines.length;
            var totalPages = Math.max(1, Math.ceil(totalLines / MODAL_PAGE_SIZE));
            modalPage = Math.min(modalPage, totalPages);

            var start = totalLines === 0 ? 0 : (modalPage - 1) * MODAL_PAGE_SIZE;
            var end = Math.min(start + MODAL_PAGE_SIZE, totalLines);
            var slice = lines.slice(start, end);

            var rowsHtml = '';
            for (var i = 0; i < slice.length; i++) {
                var l = slice[i];
                var recVal = (l.Received === null || l.Received === undefined) ? '—' : l.Received;
                var attrStr = l.AttributeSetInstance ? (' <span class="vas-174-item-attr">· ' + escapeHtml(l.AttributeSetInstance) + '</span>') : '';
                rowsHtml +=
                    '<tr>' +
                        '<td style="text-align:left;">' +
                            '<div class="vas-174-item-name">' + escapeHtml(l.ItemName || '—') + attrStr + '</div>' +
                        '</td>' +
                        '<td style="text-align:right;">' + (l.Sent || 0) + '</td>' +
                        '<td style="text-align:right;">' + recVal + '</td>' +
                        '<td style="text-align:center;">' + lineStatusPill(l.LineStatus) + '</td>' +
                    '</tr>';
            }

            var routeHeaderStr = (doc.DocumentNo || '') + ' - ' + (doc.FromWarehouse || '') + ' → ' + (doc.ToWarehouse || '');

            var showingModalTxt = totalLines === 0 ? '' :
                lbl('VAS_174_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_174_Of', 'of') + ' ' + totalLines;

            var modalHtml =
                '<div class="vas-174-modal-scrim">' +
                    '<div class="vas-174-modal">' +
                        '<div class="vas-174-modal-header">' +
                            '<span class="vas-174-modal-title" title="' + escapeHtml(routeHeaderStr) + '">' + escapeHtml(routeHeaderStr) + '</span>' +
                            pillHtml(doc.DocStatus) +
                            '<button type="button" class="vas-174-modal-close" id="vas174-m-close">&#215;</button>' +
                        '</div>' +
                        '<div class="vas-174-modal-body">' +
                            '<div class="vas-174-form-grid">' +
                                '<div class="vas-174-form-cell">' +
                                    '<span class="vas-174-form-label">' + escapeHtml(lbl('VAS_174_DocumentNo', 'Document No')) + '</span>' +
                                    '<span class="vas-174-form-val" style="color:#106AB0; font-weight:700;">' + escapeHtml(doc.DocumentNo || '—') + '</span>' +
                                '</div>' +
                                '<div class="vas-174-form-cell">' +
                                    '<span class="vas-174-form-label">' + escapeHtml(lbl('VAS_174_DocumentType', 'Document Type')) + '</span>' +
                                    '<span class="vas-174-form-val">' + escapeHtml(doc.DocTypeName || '—') + '</span>' +
                                '</div>' +
                                '<div class="vas-174-form-cell">' +
                                    '<span class="vas-174-form-label">' + escapeHtml(lbl('VAS_174_MovementDate', 'Movement Date')) + '</span>' +
                                    '<span class="vas-174-form-val">' + escapeHtml(formatDate(doc.MovementDate)) + '</span>' +
                                '</div>' +
                                '<div class="vas-174-form-cell">' +
                                    '<span class="vas-174-form-label">' + escapeHtml(lbl('VAS_174_FromTo', 'From WH → To WH')) + '</span>' +
                                    '<span class="vas-174-form-val">' + escapeHtml((doc.FromWarehouse || '') + ' → ' + (doc.ToWarehouse || '')) + '</span>' +
                                '</div>' +
                                '<div class="vas-174-form-cell">' +
                                    '<span class="vas-174-form-label">From Loc → To Loc</span>' +
                                    '<span class="vas-174-form-val">' + escapeHtml((doc.FromLocator || '') + ' → ' + (doc.ToLocator || '')) + '</span>' +
                                '</div>' +
                                '<div class="vas-174-form-cell">' +
                                    '<span class="vas-174-form-label">' + escapeHtml(lbl('VAS_174_CreatedBy', 'Created By')) + '</span>' +
                                    '<span class="vas-174-form-val">' + escapeHtml(doc.CreatedBy || '—') + '</span>' +
                                '</div>' +
                                '<div class="vas-174-form-cell">' +
                                    '<span class="vas-174-form-label">' + escapeHtml(lbl('VAS_174_RequestedBy', 'Requested By')) + '</span>' +
                                    '<span class="vas-174-form-val">' + escapeHtml(doc.RequestedBy || '—') + '</span>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-174-modal-section-heading">' +
                                '<span>' + escapeHtml(lbl('VAS_174_TransferLines', 'Transfer Lines')) + '</span>' +
                                '<span class="vas-174-count-pill">' + totalLines + '</span>' +
                            '</div>' +
                            '<div class="vas-174-modal-tbl-container">' +
                                '<table class="vas-174-modal-tbl">' +
                                    '<colgroup>' +
                                        '<col style="width: 46%;">' +
                                        '<col style="width: 18%;">' +
                                        '<col style="width: 18%;">' +
                                        '<col style="width: 18%;">' +
                                    '</colgroup>' +
                                    '<thead>' +
                                        '<tr>' +
                                            '<th style="text-align:left;">Item & Attribute</th>' +
                                            '<th style="text-align:right;">Sent</th>' +
                                            '<th style="text-align:right;">Received</th>' +
                                            '<th style="text-align:center;">Status</th>' +
                                        '</tr>' +
                                    '</thead>' +
                                    '<tbody>' + rowsHtml + '</tbody>' +
                                '</table>' +
                            '</div>' +
                            '<div class="vas-174-modal-footer">' +
                                '<span class="vas-174-footer-helper">' + escapeHtml(showingModalTxt) + '</span>' +
                                '<span class="vas-174-pager">' +
                                    '<button type="button" class="vas-174-pager-btn" id="vas174-m-prev"' + (modalPage <= 1 ? ' disabled' : '') + '>&#8249;</button>' +
                                    '<span class="vas-174-pager-label">' + modalPage + ' ' + lbl('VAS_174_Of', 'of') + ' ' + totalPages + '</span>' +
                                    '<button type="button" class="vas-174-pager-btn" id="vas174-m-next"' + (modalPage >= totalPages ? ' disabled' : '') + '>&#8250;</button>' +
                                '</span>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if ($modal) { $modal.remove(); }
            $modal = $(modalHtml);
            $('body').append($modal);

            $modal.find('#vas174-m-close').on('click', closeModal);
            $modal.find('.vas-174-modal-scrim').on('click', function (e) { if (e.target === this) { closeModal(); } });
            $modal.find('#vas174-m-prev').on('click', function () { if (modalPage > 1) { modalPage--; renderModal(); } });
            $modal.find('#vas174-m-next').on('click', function () { if (modalPage < totalPages) { modalPage++; renderModal(); } });
        }

        function closeModal() {
            if ($modal) { $modal.remove(); $modal = null; }
            activeDoc = null;
        }

        this.Initalize = function () {
            buildWidget();
            loadData();
        };

        this.refreshWidget = function () { loadData(); };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            closeModal();
            closeDropdowns();
            $(document).off('click.vas174');
            $root.off();
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_174_TransferQueueWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_174_TransferQueueWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_174_TransferQueueWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_174_TransferQueueWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_174_TransferQueueWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_174_TransferQueueWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
