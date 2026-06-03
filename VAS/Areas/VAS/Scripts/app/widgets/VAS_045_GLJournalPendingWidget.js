/**
 * GL Journal Pending Action Queue Widget
 * Purpose  : Displays GL journals awaiting user action (Draft, Approval,
 *            Post, Resubmit). Each row shows urgency marker, document info,
 *            age, and amount.
 * Tables   : GL_Journal, GL_JournalLine, C_AcctSchema, C_Currency, AD_User
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_045_PendingActionQueue, VAS_045_Items, VAS_045_Overdue
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function esc(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Precision is always dynamic from C_Currency.StdPrecision — never hardcoded.
    function fmtAmt(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        return parseFloat(amount || 0).toLocaleString(window.navigator.language, {
            minimumFractionDigits: prec,
            maximumFractionDigits: prec
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    VAS.VAS_045_GLJournalPendingWidget = function () {

        this.frame;
        this.windowNo;
        var $self   = this;
        var $root   = $('<div class="VAS-gljpq-root">');
        var baseUrl = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-gljpq-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljpq-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var clockIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<circle cx="12" cy="12" r="10"></circle>'
                + '<polyline points="12 6 12 12 16 14"></polyline>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="VAS-gljpq-card">'

                + '<div class="w-head">'
                +   '<div class="VAS-gljpq-icon">' + clockIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_045_PendingActionQueue', 'Pending Action Queue') + '</div>'
                +   '<span class="VAS-gljpq-count" id="VAS-gljpq-count-' + id + '"></span>'
                + '</div>'

                + '<div class="VAS-gljpq-body" id="VAS-gljpq-body-' + id + '"></div>'

                + '</div>';

            $root.append(html);
        }

        function loadData() {
            $.ajax({
                url      : baseUrl + 'VAS/VAS_045_GLJournalPendingWidget/GetPendingQueue',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data && data.Queue) {
                            render(data);
                        } else {
                            showError();
                        }
                    } catch (e) {
                        showError();
                    }
                    showBusy(false);
                },
                error: function () {
                    showError();
                    showBusy(false);
                }
            });
        }

        function render(data) {
            var id    = $self.AD_UserHomeWidgetID;
            var $body = $root.find('#VAS-gljpq-body-' + id);
            var queue = data.Queue || [];
            var sym   = esc(data.CurSymbol || '');
            var prec  = data.StdPrecision;

            // Update item count in header
            $root.find('#VAS-gljpq-count-' + id).text(
                data.TotalCount + ' ' + lbl('VAS_045_Items', 'items')
            );

            if (queue.length === 0) {
                $body.html('<div class="VAS-gljpq-empty">' + lbl('VIS_NoData', 'No pending journals.') + '</div>');
                return;
            }

            var html = '<div class="VAS-gljpq-list">';
            for (var i = 0; i < queue.length; i++) {
                var item = queue[i];

                var titleStr = esc(item.DocumentNo);
                if (item.Description) { titleStr += ' · ' + esc(item.Description); }

                var ageLabel = item.IsOverdue
                    ? item.AgeStr + ' ' + lbl('VAS_045_Overdue', 'overdue')
                    : item.AgeStr;

                var metaParts = [esc(item.ActionLabel), esc(ageLabel)];
                if (item.UserName) { metaParts.push(esc(item.UserName)); }

                html += '<div class="VAS-gljpq-item">'
                    +     '<div class="VAS-gljpq-mrk VAS-gljpq-mrk-' + item.MarkerType + '"></div>'
                    +     '<div class="VAS-gljpq-body-row">'
                    +       '<div class="VAS-gljpq-title">' + titleStr + '</div>'
                    +       '<div class="VAS-gljpq-meta">' + metaParts.join(' · ') + '</div>'
                    +     '</div>'
                    +     '<span class="VAS-gljpq-amt">' + esc(sym + fmtAmt(item.TotalDebit, prec)) + '</span>'
                    +   '</div>';
            }
            html += '</div>';
            $body.html(html);
        }

        function showError() {
            var id = $self.AD_UserHomeWidgetID;
            $root.find('#VAS-gljpq-body-' + id).html(
                '<div class="VAS-gljpq-empty">' + lbl('VIS_Error', 'Error loading data.') + '</div>'
            );
        }

        this.refreshWidget    = function () { showBusy(true); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.refreshWidget = function () {};

    VAS.VAS_045_GLJournalPendingWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_045_GLJournalPendingWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
