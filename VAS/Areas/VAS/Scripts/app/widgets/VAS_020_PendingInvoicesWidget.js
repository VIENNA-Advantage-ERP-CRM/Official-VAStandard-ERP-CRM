/************************************************************
 * Module Name    : VAS
 * Purpose        : Pending Invoices Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_020_PendingInvoices     => "Pending Invoices"
 *   VAS_020_NeedsAttention      => "Needs Attention"
 *   VAS_020_GRNMismatch         => "GRN Mismatch"
 *   VAS_020_PONotRaised         => "PO Not Raised"
 *   VAS_020_ReadyToPay          => "Ready to Pay"
 *   VAS_020_UpcomingPaymentsDue => "Upcoming Payments Due"
 *   VAS_020_Pending             => "pending"
 *   VAS_020_NoDuePayments       => "No payments due in the next 14 days"
 *   VAS_020_Value               => "value"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* ---- Message helper: returns the AD_Message text, or the inline default
         when the system has no message for the key. ---- */
    function msg(key, fallback) {
        var value = VIS.Msg.getMsg(key);
        return value && value !== key && value !== '[' + key + ']' ? value : fallback;
    }

    /* KPI colour palette */
    var KPI_COLORS = {
        aa:  '#D78B10',   // Awaiting Approval — amber
        grn: '#D14545',   // GRN Mismatch — red
        pnr: '#5F4AA6',   // PO Not Raised — violet
        rtp: '#019D89'    // Ready to Pay — green
    };

    VAS.VAS_020_PendingInvoicesWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self    = this;
        var $root    = $('<div class="h-100 w-100 vas-widget-bg vas-piawdg-root">');
        var $container;
        var widgetID = null;
        var $catDialog = null;
        var _catCurPrec = 2;
        var _catCat = '';
        var _catLoaded = 0;
        var _catHasMore = false;
        var _catLoading = false;
        var CAT_PAGE = 25;

        $self._kpiData = null;

        /* ---- Initialise ---- */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'visible';
        };

        /* ---- Data load ---- */
        this.intialLoad = function () {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_020_PendingInvoicesWidget/GetPendingInvoices',
                dataType: 'json',
                async: true,
                success: function (data) {
                    $self._kpiData = typeof data === 'string' ? JSON.parse(data) : data;
                    if ($self._kpiData) { renderWidget($self._kpiData); }
                    $bsyDiv[0].style.visibility = 'hidden';
                },
                error: function () {
                    $bsyDiv[0].style.visibility = 'hidden';
                }
            });
        };

        function buildShell() {
            $container = $('<div class="vas-piawdg-container" id="vas_piawdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render ---- */
        function renderWidget(data) {
            $container.empty();

            var sym  = data.CurSymbol    || '';
            var prec = VIS.Env.getCtx().getStdPrecision() || data.StdPrecision || 2;
            var tot  = data.TotalPending || 0;

            /* Header */
            var html =
                '<div class="vas-piawdg-header">' +
                    '<div class="vas-piawdg-title">' +
                        '<svg class="vas-piawdg-title-icon" viewBox="0 0 24 24" fill="none"' +
                            ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>' +
                            '<polyline points="14 2 14 8 20 8"/>' +
                            '<line x1="9" y1="15" x2="15" y2="15"/>' +
                        '</svg>' +
                        piEsc(msg('VAS_020_PendingInvoices', 'Pending Invoices')) +
                    '</div>' +
                    (tot > 0
                        ? '<span class="vas-piawdg-badge">' + tot + ' ' + (msg('VAS_020_Pending', 'pending')) + '</span>'
                        : '') +
                '</div>' +

                /* KPI 2×2 grid */
                '<div class="vas-piawdg-kpi-grid">' +
                    piKpiBox('approval',
                        msg('VAS_020_NeedsAttention', 'Needs Attention'),
                        data.AwaitingApprovalCount, data.AwaitingApprovalAmt, sym, prec, 'vas-piawdg-kpi-val--aa') +
                    piKpiBox('grn',
                        msg('VAS_020_GRNMismatch', 'GRN Mismatch'),
                        data.GrnMismatchCount, data.GrnMismatchAmt, sym, prec, 'vas-piawdg-kpi-val--grn') +
                    piKpiBox('po',
                        msg('VAS_020_PONotRaised', 'PO Not Raised'),
                        data.PoNotRaisedCount, data.PoNotRaisedAmt, sym, prec, 'vas-piawdg-kpi-val--pnr') +
                    piKpiBox('ready',
                        msg('VAS_020_ReadyToPay', 'Ready to Pay'),
                        data.ReadyToPayCount, data.ReadyToPayAmt, sym, prec, 'vas-piawdg-kpi-val--rtp') +
                '</div>' +

                '<div class="vas-piawdg-divider"></div>' +

                /* Upcoming due section */
                '<div class="vas-piawdg-due-header">' +
                    (msg('VAS_020_UpcomingPaymentsDue', 'Upcoming Payments Due')) +
                '</div>' +
                '<div class="vas-piawdg-due-list">';

            var dueItems = data.DueItems || [];
            if (dueItems.length === 0) {
                html += '<div class="vas-piawdg-due-empty">' + (msg('VAS_020_NoDuePayments', 'No payments due in the next 14 days')) + '</div>';
            } else {
                for (var i = 0; i < dueItems.length; i++) {
                    var item       = dueItems[i];
                    var urgentCls  = item.DaysUntilDue <= 3 ? 'vas-piawdg-urgent' : 'vas-piawdg-warning';
                    html +=
                        '<div class="vas-piawdg-due-item">' +
                            '<span class="vas-piawdg-due-dot ' + urgentCls + '"></span>' +
                            '<div class="vas-piawdg-due-info">' +
                                '<div class="vas-piawdg-due-name">' + piEsc(item.VendorName) + '</div>' +
                                '<div class="vas-piawdg-due-date">Due ' + piEsc(item.DueDateStr) + '</div>' +
                            '</div>' +
                            '<div class="vas-piawdg-due-amt ' + urgentCls + '">' +
                                piFmt(item.OpenAmt, sym, prec) +
                            '</div>' +
                        '</div>';
                }
            }

            html += '</div>';
            $container.html(html);

            _catCurPrec = prec;
            $container.find('.vas-piawdg-kpi-click').on('click', function () {
                openCategoryPopup($(this).attr('data-cat'), $(this).attr('data-label'));
            });
        }

        /* ---- Category drill-down popup (invoice headers behind a clicked tile) ---- */
        function openCategoryPopup(cat, label) {
            if ($catDialog) { return; }
            _catCat = cat;
            _catLoaded = 0;
            _catHasMore = false;
            _catLoading = false;

            $catDialog = $('<div class="vas-piawdg-cat-dialog">');
            $('body').append($catDialog);

            $catDialog.dialog({
                autoOpen: false,
                modal: true,
                resizable: false,
                title: label,
                width: Math.min(780, Math.max(320, $(window).width() - 40)),
                // Fixed height so the popup size never changes with the number of rows;
                // the list inside scrolls when there are more invoices than fit.
                height: Math.min(520, Math.max(320, $(window).height() - 80)),
                dialogClass: 'vas-piawdg-dialog-shell',
                close: function () {
                    $catDialog.dialog('destroy');
                    $catDialog.remove();
                    $catDialog = null;
                }
            });

            // Custom close button (in-place control we fully style, like the other popups).
            var $widget = $catDialog.dialog('widget');
            $widget.find('.ui-dialog-titlebar-close').remove();
            var $close = $('<button type="button" class="vas-piawdg-dialog-close" aria-label="' + piEsc(msg('VAS_020_Close', 'Close')) + '"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false"><path fill-rule="evenodd" clip-rule="evenodd" d="M0.331804 0.359434C0.544503 0.146971 0.832843 0.0276306 1.13348 0.0276306C1.43411 0.0276306 1.72245 0.146971 1.93515 0.359434L7.93569 6.36131L13.9376 0.359434C14.0415 0.248321 14.1667 0.159252 14.3058 0.0975303C14.4449 0.0358082 14.5949 0.00269401 14.7471 0.000157642C14.8992 -0.00237873 15.0503 0.0257147 15.1913 0.0827665C15.3324 0.139818 15.4605 0.224663 15.5681 0.332249C15.6757 0.439836 15.7605 0.567966 15.8176 0.709016C15.8746 0.850065 15.9027 1.00115 15.9002 1.15328C15.8976 1.30541 15.8645 1.45547 15.8028 1.59454C15.7411 1.73361 15.652 1.85884 15.5409 1.96278L9.53904 7.96332L15.5409 13.9652C15.742 14.1801 15.8516 14.4648 15.8467 14.759C15.8418 15.0533 15.7227 15.3341 15.5146 15.5422C15.3065 15.7504 15.0257 15.8694 14.7314 15.8743C14.4371 15.8792 14.1525 15.7696 13.9376 15.5685L7.93569 9.56667L1.93515 15.5685C1.72023 15.7696 1.43557 15.8792 1.14131 15.8743C0.847042 15.8694 0.566203 15.7504 0.358097 15.5422C0.149991 15.3341 0.0309118 15.0533 0.0260057 14.759C0.0210996 14.4648 0.130751 14.1801 0.331804 13.9652L6.33234 7.96332L0.331804 1.96278C0.119341 1.75008 0 1.46174 0 1.16111C0 0.860474 0.119341 0.572134 0.331804 0.359434Z"></path></svg></button>');
            $close.on('click', function () { if ($catDialog) { $catDialog.dialog('close'); } });
            $widget.find('.ui-dialog-titlebar').append($close);

            $catDialog.html('<div class="vas-piawdg-pop-state">' + piEsc(msg('VAS_020_Loading', 'Loading...')) + '</div>');
            $catDialog.dialog('open');
            $catDialog.dialog('option', 'position', { my: 'center', at: 'center', of: window });

            // Load only the first page; more pages load as the user scrolls.
            fetchCatPage(0, function (items) {
                if (!$catDialog) { return; }
                _catLoaded = items.length;
                _catHasMore = items.length === CAT_PAGE;
                renderCatFresh(items);
            }, function () {
                if (!$catDialog) { return; }
                $catDialog.html('<div class="vas-piawdg-pop-state vas-piawdg-pop-error">' + piEsc(msg('VAS_020_LoadError', 'Unable to load invoices.')) + '</div>');
            });
        }

        function fetchCatPage(offset, onOk, onErr) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_020_PendingInvoicesWidget/GetCategoryInvoices',
                data: { category: _catCat, maxRows: CAT_PAGE, offset: offset },
                dataType: 'json',
                async: true,
                success: function (res) {
                    var data = null;
                    try { data = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    onOk(data ? (data.Items || []) : []);
                },
                error: function () { onErr(); }
            });
        }

        function renderCatFresh(items) {
            if (!$catDialog) { return; }
            if (!items || items.length === 0) {
                $catDialog.html('<div class="vas-piawdg-pop-state">' + piEsc(msg('VAS_020_NoInvoices', 'No invoices found.')) + '</div>');
                return;
            }

            var html = '<div class="vas-piawdg-pop">' +
                '<div class="vas-piawdg-pop-count"></div>' +
                '<div class="vas-piawdg-pop-list">' +
                    '<div class="vas-piawdg-pop-rows">';
            for (var i = 0; i < items.length; i++) { html += buildCatRow(items[i]); }
            html += '</div>' +
                    '<div class="vas-piawdg-pop-more"><span class="vas-piawdg-pop-more-spin"></span></div>' +
                '</div></div>';
            $catDialog.html(html);
            updateCatCount();
            $catDialog.find('.vas-piawdg-pop-list').on('scroll', onCatScroll);
        }

        // Row layout matches the AP / GL search popup: kind chip, then DocNo + status
        // pill over the vendor, then amount (transaction currency) over the date.
        function buildCatRow(it) {
            var st = piStatusMeta(it.DocStatus);
            return '<div class="vas-piawdg-pop-row">' +
                '<span class="vas-piawdg-pop-chip">' + piEsc(msg('VAS_020_Kind', 'Invoice')) + '</span>' +
                '<div class="vas-piawdg-pop-main">' +
                    '<div class="vas-piawdg-pop-docline">' +
                        '<span class="vas-piawdg-pop-docno">' + piEsc(it.DocumentNo || '') + '</span>' +
                        '<span class="vas-piawdg-pop-status vas-piawdg-pop-status-' + st.tone + '">' + piEsc(st.label) + '</span>' +
                    '</div>' +
                    '<div class="vas-piawdg-pop-title">' + piEsc(it.VendorName || '') + '</div>' +
                '</div>' +
                '<div class="vas-piawdg-pop-meta">' +
                    '<div class="vas-piawdg-pop-amount">' + piEsc(piCatAmt(it.Amount, it.CurCode)) + '</div>' +
                    '<div class="vas-piawdg-pop-date">' + piEsc(piDate(it.DocDate)) + '</div>' +
                '</div>' +
            '</div>';
        }

        function appendCatRows(items) {
            if (!items || items.length === 0 || !$catDialog) { return; }
            var html = '';
            for (var i = 0; i < items.length; i++) { html += buildCatRow(items[i]); }
            $catDialog.find('.vas-piawdg-pop-rows').append(html);
        }

        function updateCatCount() {
            if (!$catDialog) { return; }
            var text = _catLoaded + (_catHasMore ? '+' : '') + ' ' + msg('VAS_020_Invoices', 'invoices');
            $catDialog.find('.vas-piawdg-pop-count').text(text);
        }

        function onCatScroll() {
            if (!_catHasMore || _catLoading || !$catDialog) { return; }
            var el = $catDialog.find('.vas-piawdg-pop-list')[0];
            if (!el) { return; }
            if (el.scrollTop + el.clientHeight >= el.scrollHeight - 48) { loadMoreCat(); }
        }

        function loadMoreCat() {
            if (_catLoading || !_catHasMore || !$catDialog) { return; }
            _catLoading = true;
            var atCat = _catCat;
            $catDialog.find('.vas-piawdg-pop-more').addClass('vas-piawdg-pop-more-active');
            fetchCatPage(_catLoaded, function (items) {
                if (!$catDialog || atCat !== _catCat) { _catLoading = false; return; }
                appendCatRows(items);
                _catLoaded += items.length;
                _catHasMore = items.length === CAT_PAGE;
                _catLoading = false;
                $catDialog.find('.vas-piawdg-pop-more').removeClass('vas-piawdg-pop-more-active');
                updateCatCount();
            }, function () {
                _catLoading = false;
                if ($catDialog) { $catDialog.find('.vas-piawdg-pop-more').removeClass('vas-piawdg-pop-more-active'); }
            });
        }

        function piDate(iso) {
            if (!iso) { return ''; }
            var p = String(iso).split('-');
            if (p.length !== 3) { return iso; }
            var d = new Date(parseInt(p[0], 10), parseInt(p[1], 10) - 1, parseInt(p[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }

        function piCatAmt(amount, curCode) {
            var n = (typeof amount === 'number') ? amount : parseFloat(amount);
            if (isNaN(n)) { n = 0; }
            var prec = _catCurPrec || 2;
            var formatted = n.toLocaleString(window.navigator.language, { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return (curCode ? curCode + ' ' : '') + formatted;
        }

        function piStatusMeta(code) {
            var map = {
                CO: ['Completed', 'ok'], CL: ['Closed', 'ok'], AP: ['Approved', 'info'],
                DR: ['Draft', 'muted'], IP: ['In Process', 'warn'], WC: ['Waiting Confirm', 'warn'],
                WP: ['Waiting Payment', 'warn'], NA: ['Not Approved', 'err'], IN: ['Invalid', 'err'],
                VO: ['Voided', 'err'], RE: ['Reversed', 'err']
            };
            var c = String(code || '').toUpperCase();
            var m = map[c] || [code || '', 'muted'];
            return { label: m[0], tone: m[1] };
        }

        /* ---- Build a KPI box (clickable: opens the invoice drill-down popup) ---- */
        function piKpiBox(cat, label, count, amount, sym, prec, colorClass) {
            return (
                '<div class="vas-piawdg-kpi-box vas-piawdg-kpi-click" data-cat="' + cat + '" data-label="' + piEsc(label) + '" role="button" tabindex="0">' +
                    '<div class="vas-piawdg-kpi-lbl">' + piEsc(label) + '</div>' +
                    '<div class="vas-piawdg-kpi-val ' + colorClass + '">' + (count || 0) + '</div>' +
                    '<div class="vas-piawdg-kpi-sub">' + piFmt(amount || 0, sym, prec) + ' ' + (msg('VAS_020_Value', 'value')) + '</div>' +
                '</div>'
            );
        }

        /* ---- Helpers ---- */
        function piEsc(str) {
            return String(str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function piFmt(amount, sym, precision) {
            if (!amount || amount === 0) { return sym + '0'; }
            var abs  = Math.abs(amount);
            var loc  = window.navigator.language;
            var opts1 = { minimumFractionDigits: 1, maximumFractionDigits: 1 };
            var prec  = VIS.Env.getCtx().getStdPrecision() || precision || 2;
            if (abs >= 10000000) { return sym + (abs / 10000000).toLocaleString(loc, opts1) + 'Cr'; }
            if (abs >= 100000)   { return sym + (abs / 100000).toLocaleString(loc, opts1)   + 'L';  }
            if (abs >= 1000)     { return sym + (abs / 1000).toLocaleString(loc, opts1)     + 'K';  }
            return sym + abs.toLocaleString(loc, { minimumFractionDigits: prec, maximumFractionDigits: prec });
        }

        /* ---- Busy indicator ---- */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'visible';
            $root.append($bsyDiv);
        }

        /* ---- Refresh ---- */
        this.refreshWidget = function () {
            if ($catDialog) { try { $catDialog.dialog('close'); } catch (e) { } }
            $self._kpiData = null;
            $bsyDiv[0].style.visibility = 'visible';
            $container.empty();
            $self.intialLoad();
        };

        this._teardown = function () {
            if ($catDialog) { try { $catDialog.dialog('close'); } catch (e) { } }
        };

        this.getRoot = function () { return $root; };
    };

    /* ---- Prototype ---- */
    VAS.VAS_020_PendingInvoicesWidget.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_020_PendingInvoicesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_020_PendingInvoicesWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_020_PendingInvoicesWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
