/************************************************************
 * Module Name    : VAS
 * Purpose        : Vendor Credit Utilisation Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_026_VendorCreditUtil  => "Vendor Credit Utilisation"
 *   VAS_026_Breached          => "breached"
 *   VAS_026_BreachedChip      => "Breached"
 *   VAS_026_NoCreditVendors   => "No vendors with a credit limit configured"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* ---- Message helper: returns the AD_Message text, or the inline default
         when the system has no message for the key. ---- */
    function msg(key, fallback) {
        var value = VIS.Msg.getMsg(key);
        return value && value !== key && value !== '[' + key + ']' ? value : fallback;
    }

    VAS.VAS_026_VendorCreditUtilisationWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self    = this;
        var $root    = $('<div class="h-100 w-100 vas-widget-bg vas-vcuwdg-root">');
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
                url: VIS.Application.contextUrl + 'VAS/VAS_026_VendorCreditUtilisationWidget/GetVendorCreditUtilisation',
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
            $container = $('<div class="vas-vcuwdg-container" id="vas_vcuwdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render ---- */
        function renderWidget(data) {
            $container.empty();

            var sym      = data.CurSymbol    || '';
            var prec     = VIS.Env.getCtx().getStdPrecision() || data.StdPrecision || 2;
            var vendors  = data.Vendors      || [];
            var breached = data.BreachCount  || 0;

            /* Header */
            var html =
                '<div class="vas-vcuwdg-header">' +
                    '<div class="vas-vcuwdg-title">' +
                        '<svg class="vas-vcuwdg-title-icon" viewBox="0 0 24 24" fill="none"' +
                            ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<rect x="1" y="4" width="22" height="16" rx="2"/>' +
                            '<line x1="1" y1="10" x2="23" y2="10"/>' +
                        '</svg>' +
                        vcuEsc(msg('VAS_026_VendorCreditUtil', 'Vendor Credit Utilisation')) +
                    '</div>' +
                    (breached > 0
                        ? '<span class="vas-vcuwdg-badge">' + breached + ' ' +
                          vcuEsc(msg('VAS_026_Breached', 'breached')) + '</span>'
                        : '') +
                '</div>' +
                '<div class="vas-vcuwdg-list">';

            if (vendors.length === 0) {
                html += '<div class="vas-vcuwdg-empty">' + (msg('VAS_026_NoCreditVendors', 'No vendors with a credit limit configured')) + '</div>';
            } else {
                for (var i = 0; i < vendors.length; i++) {
                    html += vcuBuildRow(vendors[i], sym, prec);
                }
            }

            html += '</div>';
            $container.html(html);
        }

        /* ---- Build a progress row ---- */
        function vcuBuildRow(v, sym, prec) {
            var pct        = Math.max(0, v.UtilPct);
            var fillW      = Math.min(100, pct);
            var barClass   = pct >= 100 ? 'vas-vcuwdg-prog-fill--red' : (pct >= 75 ? 'vas-vcuwdg-prog-fill--amber' : 'vas-vcuwdg-prog-fill--green');
            var valClass   = pct >= 100 ? 'vas-vcuwdg-prog-val--breached' : 'vas-vcuwdg-prog-val--normal';
            var breachChip = v.IsBreached
                ? '<span class="vas-vcuwdg-breach-chip">' +
                  vcuEsc(msg('VAS_026_BreachedChip', 'Breached')) + '</span>'
                : '';
            var pctStr = Math.round(pct) + '%';
            var amtStr = vcuFmt(v.CreditUsed, sym, prec) + '/' + vcuFmt(v.CreditLimit, sym, prec);

            return (
                '<div class="vas-vcuwdg-prog">' +
                    '<div class="vas-vcuwdg-prog-meta">' +
                        '<span class="vas-vcuwdg-prog-name">' +
                            vcuEsc(v.VendorName) + breachChip +
                        '</span>' +
                        '<span class="vas-vcuwdg-prog-val ' + valClass + '">' +
                            vcuEsc(pctStr + ' · ' + amtStr) +
                        '</span>' +
                    '</div>' +
                    '<div class="vas-vcuwdg-prog-track">' +
                        '<div class="vas-vcuwdg-prog-fill ' + barClass + '" style="width:' + fillW + '%"></div>' +
                    '</div>' +
                '</div>'
            );
        }

        /* ---- Helpers ---- */
        function vcuEsc(str) {
            return String(str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function vcuFmt(amount, sym, precision) {
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
    VAS.VAS_026_VendorCreditUtilisationWidget.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_026_VendorCreditUtilisationWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_026_VendorCreditUtilisationWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_026_VendorCreditUtilisationWidget.prototype.dispose = function () {
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
