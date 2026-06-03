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
                        piEsc(VIS.Msg.getMsg('VAS_020_PendingInvoices') || 'Pending Invoices') +
                    '</div>' +
                    (tot > 0
                        ? '<span class="vas-piawdg-badge">' + tot + ' ' + (VIS.Msg.getMsg('VAS_020_Pending') || 'pending') + '</span>'
                        : '') +
                '</div>' +

                /* KPI 2×2 grid */
                '<div class="vas-piawdg-kpi-grid">' +
                    piKpiBox(
                        VIS.Msg.getMsg('VAS_020_NeedsAttention') || 'Needs Attention',
                        data.AwaitingApprovalCount, data.AwaitingApprovalAmt, sym, prec, 'vas-piawdg-kpi-val--aa') +
                    piKpiBox(
                        VIS.Msg.getMsg('VAS_020_GRNMismatch') || 'GRN Mismatch',
                        data.GrnMismatchCount, data.GrnMismatchAmt, sym, prec, 'vas-piawdg-kpi-val--grn') +
                    piKpiBox(
                        VIS.Msg.getMsg('VAS_020_PONotRaised') || 'PO Not Raised',
                        data.PoNotRaisedCount, data.PoNotRaisedAmt, sym, prec, 'vas-piawdg-kpi-val--pnr') +
                    piKpiBox(
                        VIS.Msg.getMsg('VAS_020_ReadyToPay') || 'Ready to Pay',
                        data.ReadyToPayCount, data.ReadyToPayAmt, sym, prec, 'vas-piawdg-kpi-val--rtp') +
                '</div>' +

                '<div class="vas-piawdg-divider"></div>' +

                /* Upcoming due section */
                '<div class="vas-piawdg-due-header">' +
                    (VIS.Msg.getMsg('VAS_020_UpcomingPaymentsDue') || 'Upcoming Payments Due') +
                '</div>' +
                '<div class="vas-piawdg-due-list">';

            var dueItems = data.DueItems || [];
            if (dueItems.length === 0) {
                html += '<div class="vas-piawdg-due-empty">' + (VIS.Msg.getMsg('VAS_020_NoDuePayments') || 'No payments due in the next 14 days') + '</div>';
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
        }

        /* ---- Build a KPI box ---- */
        function piKpiBox(label, count, amount, sym, prec, colorClass) {
            return (
                '<div class="vas-piawdg-kpi-box">' +
                    '<div class="vas-piawdg-kpi-lbl">' + piEsc(label) + '</div>' +
                    '<div class="vas-piawdg-kpi-val ' + colorClass + '">' + (count || 0) + '</div>' +
                    '<div class="vas-piawdg-kpi-sub">' + piFmt(amount || 0, sym, prec) + ' ' + (VIS.Msg.getMsg('VAS_020_Value') || 'value') + '</div>' +
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
            $self._kpiData = null;
            $bsyDiv[0].style.visibility = 'visible';
            $container.empty();
            $self.intialLoad();
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
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
