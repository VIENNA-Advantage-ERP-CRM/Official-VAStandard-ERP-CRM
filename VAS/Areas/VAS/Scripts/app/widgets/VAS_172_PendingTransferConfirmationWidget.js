/**
 * Pending Transfer Confirmation Widget (Material Transfer Dashboard)
 * Purpose - 3x2 operational work-queue list widget with a two-step confirmation modal.
 *           Lists pending transfer documents awaiting destination confirmation.
 * Prefix  - VAS_172_
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
        return lbl('VAS_172_Months', 'Jan,Feb,Mar,Apr,May,Jun,Jul,Aug,Sep,Oct,Nov,Dec').split(',');
    }

    function formatDate(iso) {
        if (!iso) { return '—'; }
        var d = new Date(iso);
        if (isNaN(d)) { return iso; }
        return d.getDate() + ' ' + monthNames()[d.getMonth()] + ' ' + d.getFullYear();
    }

    function deriveLineStatus(confirmed, target) {
        if (confirmed === null || confirmed === undefined) {
            return { status: 'Pending', cls: 'vas-172-pill--amber', text: 'Pending' };
        }
        var c = Number(confirmed);
        var t = Number(target);
        if (c === t) {
            return { status: 'Matched', cls: 'vas-172-pill--ok', text: 'Matched' };
        } else if (c < t) {
            return { status: 'Short', cls: 'vas-172-pill--bad', text: 'Short (' + (t - c) + ')' };
        } else {
            return { status: 'Over', cls: 'vas-172-pill--amber', text: 'Over (+' + (c - t) + ')' };
        }
    }

    VAS.VAS_172_PendingTransferConfirmationWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-172-container">');
        var $root = $('<div class="vas-172-root vas-172-card">');

        var allQueueRecords = [];
        var PAGE_SIZE = 4;
        var currentPage = 1;

        var $modal = null;
        var activeTransfer = null;
        var activeLine = null;

        function buildWidget() {
            $root.html(
                '<div class="vas-172-header-row">' +
                    '<div class="vas-172-left-cluster">' +
                        '<div class="vas-172-icon-well">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<polyline points="20 6 9 17 4 12"></polyline>' +
                            '</svg>' +
                        '</div>' +
                        '<div class="vas-172-title-block">' +
                            '<div class="vas-172-title">' + escapeHtml(lbl('VAS_172_PendingTransferConfirmation', 'Pending Transfer Confirmation')) + '</div>' +
                            '<div class="vas-172-subtitle">' + escapeHtml(lbl('VAS_172_RequiringConfirmation', 'Requiring confirmation')) + '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>' +
                '<div class="vas-172-body"></div>' +
                '<div class="vas-172-footer"></div>'
            );

            $wrapper.append($root);
        }

        function renderQueue() {
            var $body = $root.find('.vas-172-body');
            var $footer = $root.find('.vas-172-footer');
            var total = allQueueRecords.length;
            var totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
            currentPage = Math.min(currentPage, totalPages);

            if (total === 0) {
                $body.html('<div class="vas-172-empty">' + escapeHtml(lbl('VAS_172_NoPendingConfirmations', 'No pending confirmations')) + '</div>');
                renderFooter($footer, 0, 0, 0, 1, 1);
                return;
            }

            var start = (currentPage - 1) * PAGE_SIZE;
            var end = Math.min(start + PAGE_SIZE, total);
            var slice = allQueueRecords.slice(start, end);

            var html = '<div class="vas-172-queue">';
            for (var i = 0; i < slice.length; i++) {
                var item = slice[i];
                var routeStr = (item.SourceLocator || item.SourceWarehouse || 'Src') + ' → ' + (item.DestLocator || item.DestWarehouse || 'Dst') + ' · ' + (item.LineCount || 0) + ' ' + (item.LineCount === 1 ? 'line' : 'lines');
                var statusPill = '<span class="vas-172-pill vas-172-pill--amber">Pending</span>';

                html += '<button type="button" class="vas-172-row' + (i === slice.length - 1 ? ' vas-172-row--last' : '') + '" data-idx="' + (start + i) + '">' +
                    '<div class="vas-172-row-top">' +
                        '<span class="vas-172-trno">' + escapeHtml(item.TransferNo || '—') + '</span>' +
                        statusPill +
                    '</div>' +
                    '<div class="vas-172-row-meta">' + escapeHtml(routeStr) + '</div>' +
                '</button>';
            }
            html += '</div>';
            $body.html(html);

            $body.find('.vas-172-row').on('click', function () {
                var idx = parseInt($(this).attr('data-idx'), 10);
                openModal(allQueueRecords[idx]);
            });

            renderFooter($footer, start + 1, end, total, currentPage, totalPages);
        }

        function renderFooter($footer, s, e, total, page, totalPages) {
            var showingTxt = total === 0 ? '' :
                lbl('VAS_172_Showing', 'Showing') + ' ' + s + '–' + e + ' ' + lbl('VAS_172_Of', 'of') + ' ' + total;
            $footer.html(
                '<span class="vas-172-footer-helper">' + escapeHtml(showingTxt) + '</span>' +
                '<span class="vas-172-pager">' +
                    '<button type="button" class="vas-172-pager-btn" id="vas172-prev"' + (page <= 1 ? ' disabled' : '') + '>&#8249;</button>' +
                    '<span class="vas-172-pager-label">' + page + ' ' + lbl('VAS_172_Of', 'of') + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-172-pager-btn" id="vas172-next"' + (page >= totalPages ? ' disabled' : '') + '>&#8250;</button>' +
                '</span>'
            );
            $footer.find('#vas172-prev').on('click', function () { if (currentPage > 1) { currentPage--; renderQueue(); } });
            $footer.find('#vas172-next').on('click', function () { if (currentPage < totalPages) { currentPage++; renderQueue(); } });
        }

        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_172_PendingTransferConfirmationWidget/GetPendingConfirmations',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }
                    allQueueRecords = (data && !data.error && Array.isArray(data.records)) ? data.records : [];
                    currentPage = 1;
                    renderQueue();
                },
                error: function () {
                    allQueueRecords = [];
                    renderQueue();
                }
            });
        }

        function openModal(tr) {
            activeTransfer = tr;
            renderModalView1();
        }

        function closeModal() {
            if ($modal) { $modal.remove(); $modal = null; }
            activeTransfer = null;
            activeLine = null;
        }

        function renderModalView1() {
            var tr = activeTransfer;
            var lines = tr.Lines || [];
            var allMatched = (lines.length > 0);

            var linesHtml = '';
            for (var i = 0; i < lines.length; i++) {
                var l = lines[i];
                var s = deriveLineStatus(l.ConfirmedQty, l.TargetQty);
                if (s.status !== 'Matched') { allMatched = false; }

                var confStr = (l.ConfirmedQty === null || l.ConfirmedQty === undefined) ? '–' : l.ConfirmedQty;
                linesHtml +=
                    '<button type="button" class="vas-172-line-row" data-lidx="' + i + '">' +
                        '<div class="vas-172-line-left">' +
                            '<div class="vas-172-line-no">' + escapeHtml('Line ' + (l.LineNo || (i + 1))) + '</div>' +
                            '<div class="vas-172-line-sub">' + escapeHtml((l.ItemName || 'Item') + ' · ' + (l.UOM || '')) + '</div>' +
                        '</div>' +
                        '<div class="vas-172-line-count">' + confStr + ' / ' + l.TargetQty + '</div>' +
                        '<span class="vas-172-pill ' + s.cls + '">' + escapeHtml(s.text) + '</span>' +
                        '<span class="vas-172-chevron">&#8250;</span>' +
                    '</button>';
            }

            var modalHtml =
                '<div class="vas-172-modal-scrim">' +
                    '<div class="vas-172-modal">' +
                        '<div class="vas-172-modal-header">' +
                            '<span class="vas-172-modal-title">' + escapeHtml((tr.TransferNo || 'TR') + ' · Transfer Confirmation') + '</span>' +
                            '<span class="vas-172-pill vas-172-pill--amber">Pending</span>' +
                            '<button type="button" class="vas-172-modal-close" id="vas172-m-close">&#215;</button>' +
                        '</div>' +
                        '<div class="vas-172-modal-body">' +
                            '<button type="button" class="vas-172-back-link" id="vas172-b1">&#8249; Back to pending confirmations</button>' +
                            '<div class="vas-172-field-grid">' +
                                '<div class="vas-172-field">' +
                                    '<div class="vas-172-field-label">Source Transfer</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(tr.TransferNo || '—') + '</div>' +
                                '</div>' +
                                '<div class="vas-172-field">' +
                                    '<div class="vas-172-field-label">Route</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml((tr.SourceLocator || '') + ' → ' + (tr.DestLocator || '')) + '</div>' +
                                '</div>' +
                                '<div class="vas-172-field">' +
                                    '<div class="vas-172-field-label">From → To Warehouse</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml((tr.SourceWarehouse || '') + ' → ' + (tr.DestWarehouse || '')) + '</div>' +
                                '</div>' +
                                '<div class="vas-172-field">' +
                                    '<div class="vas-172-field-label">Date</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(formatDate(tr.MovementDate)) + '</div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-172-section-heading">Confirmation Lines</div>' +
                            '<div class="vas-172-lines-list-container">' +
                                '<div class="vas-172-lines-list">' + linesHtml + '</div>' +
                            '</div>' +
                            '<div class="vas-172-action-bar">' +
                                '<button type="button" class="vas-172-btn-complete' + (!allMatched ? ' vas-172-btn-complete--disabled' : '') + '" id="vas172-btn-comp"' + (!allMatched ? ' disabled' : '') + '>Complete Transfer Confirmation</button>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if ($modal) { $modal.remove(); }
            $modal = $(modalHtml);
            $('body').append($modal);

            $modal.find('#vas172-m-close').on('click', closeModal);
            $modal.find('#vas172-b1').on('click', closeModal);
            $modal.find('.vas-172-modal-scrim').on('click', function (e) { if (e.target === this) { closeModal(); } });

            $modal.find('.vas-172-line-row').on('click', function () {
                var lidx = parseInt($(this).attr('data-lidx'), 10);
                activeLine = lines[lidx];
                renderModalView2();
            });

            $modal.find('#vas172-btn-comp').on('click', function () {
                if (!allMatched) { return; }
                completeConfirmation();
            });
        }

        function renderModalView2() {
            var tr = activeTransfer;
            var l = activeLine;

            var prefillConf = (l.ConfirmedQty === null || l.ConfirmedQty === undefined) ? l.TargetQty : l.ConfirmedQty;
            var initialDiff = Number(l.TargetQty || 0) - Number(prefillConf || 0);
            var diffCls = initialDiff === 0 ? 'vas-172-diff--ok' : 'vas-172-diff--bad';

            var modalHtml =
                '<div class="vas-172-modal-scrim">' +
                    '<div class="vas-172-modal">' +
                        '<div class="vas-172-modal-header">' +
                            '<span class="vas-172-modal-title">Review Confirmation Line</span>' +
                            '<button type="button" class="vas-172-modal-close" id="vas172-m-close2">&#215;</button>' +
                        '</div>' +
                        '<div class="vas-172-modal-body">' +
                            '<button type="button" class="vas-172-back-link" id="vas172-b2">&#8249; Back to ' + escapeHtml(tr.TransferNo || 'TR') + '</button>' +
                            '<div class="vas-172-v2-grid">' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">Transfer Line</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml('Line ' + (l.LineNo || 1) + ' · ' + (l.ItemName || 'Item')) + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">UOM</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(l.UOM || '—') + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">Attribute Set Instance</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(l.AttributeSetInstance || '—') + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">Scrap Locator</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(l.ScrapLocator || '—') + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">Target Quantity</div>' +
                                    '<div class="vas-172-field-value" style="font-weight:700;">' + l.TargetQty + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-editable">' +
                                    '<div class="vas-172-field-label">Confirmed Quantity *</div>' +
                                    '<input type="number" step="any" class="vas-172-input" id="vas172-inp-conf" value="' + prefillConf + '">' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">Difference</div>' +
                                    '<div class="vas-172-field-value ' + diffCls + '" id="vas172-diff-val">' + initialDiff + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-editable vas-172-v2-span2" style="grid-row: span 2;">' +
                                    '<div class="vas-172-field-label">Description / Note</div>' +
                                    '<textarea class="vas-172-textarea" id="vas172-inp-desc" placeholder="Add a note (optional)">' + escapeHtml(l.Description || '') + '</textarea>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-editable">' +
                                    '<div class="vas-172-field-label">Scrapped Quantity</div>' +
                                    '<input type="number" step="any" class="vas-172-input" id="vas172-inp-scrap" value="' + (l.ScrappedQty || 0) + '">' +
                                '</div>' +
                            '</div>' +
                            '<div style="margin-top:0.75rem; text-align:right;">' +
                                '<button type="button" class="vas-172-btn-save" id="vas172-btn-save">Save Line</button>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if ($modal) { $modal.remove(); }
            $modal = $(modalHtml);
            $('body').append($modal);

            $modal.find('#vas172-m-close2').on('click', closeModal);
            $modal.find('#vas172-b2').on('click', function () { renderModalView1(); });
            $modal.find('.vas-172-modal-scrim').on('click', function (e) { if (e.target === this) { closeModal(); } });

            var calcDiff = function () {
                var target = Number(l.TargetQty || 0);
                var confVal = parseFloat($modal.find('#vas172-inp-conf').val());
                if (isNaN(confVal)) { confVal = 0; }
                var diff = target - confVal;
                var $d = $modal.find('#vas172-diff-val');
                $d.text(diff);
                if (diff === 0) {
                    $d.attr('class', 'vas-172-field-value vas-172-diff--ok');
                } else {
                    $d.attr('class', 'vas-172-field-value vas-172-diff--bad');
                }
            };

            $modal.find('#vas172-inp-conf').on('input change', calcDiff);

            $modal.find('#vas172-btn-save').on('click', function () {
                var confVal = parseFloat($modal.find('#vas172-inp-conf').val());
                var scrapVal = parseFloat($modal.find('#vas172-inp-scrap').val());
                var descVal = $modal.find('#vas172-inp-desc').val();

                if (isNaN(confVal) || confVal < 0) {
                    alert('Confirmed Quantity must be a valid non-negative number.');
                    return;
                }
                if (isNaN(scrapVal) || scrapVal < 0) { scrapVal = 0; }

                l.ConfirmedQty = confVal;
                l.ScrappedQty = scrapVal;
                l.Description = descVal;

                $.ajax({
                    url: VIS.Application.contextUrl + 'VAS_172_PendingTransferConfirmationWidget/SaveConfirmationLine',
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify({
                        LineID: l.LineID || 0,
                        TransferID: tr.TransferID || 0,
                        LineNo: l.LineNo || 1,
                        ConfirmedQty: confVal,
                        ScrappedQty: scrapVal,
                        Description: descVal
                    }),
                    success: function () {
                        renderModalView1();
                    },
                    error: function () {
                        renderModalView1();
                    }
                });
            });
        }

        function completeConfirmation() {
            var tr = activeTransfer;
            if (!tr) { return; }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_172_PendingTransferConfirmationWidget/CompleteTransfer',
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({
                    TransferID: tr.TransferID || 0
                }),
                success: function () {
                    closeModal();
                    loadData();
                },
                error: function () {
                    closeModal();
                    loadData();
                }
            });
        }

        this.Initalize = function () {
            buildWidget();
            loadData();
        };

        this.refreshWidget = function () { loadData(); };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            closeModal();
            $root.off();
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
