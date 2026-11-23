/**
 * Shared child-tab navigation helper for VAS tab panels (right-hand panels hosted in a window).
 *
 * VAS.TabNavUtil.openChildRecord(panel, tableName, recordId, options)
 *   Opens ONE record of a child tab IN THE WINDOW THE PANEL SITS IN (no second window):
 *     1. finds the hosting window controller (APanel) of the panel,
 *     2. finds the child tab of that window for the table (e.g. 'C_OrderLine'),
 *     3. switches the window to that tab - the same call a click on the tab header makes,
 *        so the current parent record stays the parent and the tab loads its children,
 *     4. selects the record's row once the tab's rows have arrived (the tab change
 *        re-queries asynchronously and gives no callback, so the row is polled for),
 *     5. optionally switches the tab to single-row view (the toolbar's single-row button),
 *        so the record's detail form shows.
 *   Arguments:
 *     panel      - the tab panel instance (needs .curTab and .windowNo; $self in a panel).
 *     tableName  - table of the child tab, e.g. 'C_OrderLine'. The tab is matched on its table
 *                  name or on its key column (tableName + '_ID').
 *     recordId   - the child record's id (e.g. C_OrderLine_ID).
 *     options    - { singleRow: true (default) | false, timeoutMs: 6000, pollMs: 100,
 *                    logPrefix: 'VAS' } (all optional).
 *   Returns a promise resolved with true when the record ended up selected, false otherwise
 *   (host window / tab not found, or the record is not among the rows the tab loaded, e.g.
 *   it is on a later page). It never throws, a click can never break.
 *
 * Relies on internal VIS members (curTab.mDataListenerList, listener.aPanel, aPanel.onTabChange,
 * aPanel.curGC, aPanel.aSingle, GridController.navigate / switchSingleRow) - the same ones
 * VAS_293_BankAccountRightPanel uses. Nothing is hard-coded: window and tab ids come from
 * the window's own metadata.
 *
 * Labels / Message Keys - none (no user-facing text).
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.TabNavUtil = VAS.TabNavUtil || {};

    var DEFAULT_POLL_MS = 100;
    var DEFAULT_TIMEOUT_MS = 6000;

    function log(prefix, text) {
        if (window.console) console.log(prefix + ': ' + text);
    }

    /* The APanel hosting the panel's window. The host never hands it over directly, but it
       registers its GridController as a data-status listener on the tab
       (GridController.initGrid -> gTab.addDataStatusListener) and that controller carries the
       APanel as .aPanel. */
    function findHostPanel(tab, prefix) {
        var list = tab && tab.mDataListenerList;
        if (!list || !list.length) {
            log(prefix, 'no curTab / data listeners (curTab=' + !!tab + ')');
            return null;
        }
        var fallback = null;
        for (var i = 0; i < list.length; i++) {
            var l = list[i];
            if (l && l.aPanel && typeof l.aPanel.onTabChange === 'function') {
                if (l.gTab === tab) return l.aPanel;
                if (!fallback) fallback = l.aPanel;
            }
        }
        if (!fallback) log(prefix, 'no APanel among ' + list.length + ' data listeners');
        return fallback;
    }

    /* The window's tab for the table: matched on table name or key column; a tab deeper than
       the panel's own tab is preferred when the table appears more than once. */
    function findTab(aPanel, curTab, tableName, prefix) {
        var gw = (aPanel && aPanel.gridWindow) || (curTab && curTab.gridWindow);
        var tabs = (gw && typeof gw.getTabs === 'function') ? gw.getTabs() : null;
        if (!tabs) {
            log(prefix, 'window tab list not available');
            return null;
        }
        var table = String(tableName).toUpperCase();
        var key = table + '_ID';
        var curLevel = (curTab && typeof curTab.getTabLevel === 'function') ? (+curTab.getTabLevel() || 0) : 0;
        var found = null, foundIsChild = false, names = [];
        for (var i = 0; i < tabs.length; i++) {
            var t = tabs[i];
            if (!t || t === curTab) continue;
            var tn = String(t.getTableName() || '').toUpperCase();
            var kc = String(typeof t.getKeyColumnName === 'function' ? (t.getKeyColumnName() || '') : '').toUpperCase();
            names.push(tn + '/' + kc);
            if (tn === table || kc === key) {
                var isChild = (+t.getTabLevel() || 0) > curLevel;
                if (!found || (isChild && !foundIsChild)) {
                    found = t;
                    foundIsChild = isChild;
                }
            }
        }
        if (!found) log(prefix, 'no ' + tableName + ' tab among: ' + names.join(', '));
        return found;
    }

    VAS.TabNavUtil.openChildRecord = function (panel, tableName, recordId, options) {
        var deferred = $.Deferred();
        var opts = options || {};
        var prefix = opts.logPrefix || 'VAS';
        var pollMs = +opts.pollMs > 0 ? +opts.pollMs : DEFAULT_POLL_MS;
        var timeoutMs = +opts.timeoutMs > 0 ? +opts.timeoutMs : DEFAULT_TIMEOUT_MS;
        var singleRow = opts.singleRow !== false;
        var record = +recordId || 0;

        try {
            if (!panel || !tableName || record <= 0) {
                return deferred.resolve(false).promise();
            }

            var curTab = panel.curTab;
            var aPanel = findHostPanel(curTab, prefix);
            var tab = aPanel ? findTab(aPanel, curTab, tableName, prefix) : null;
            if (!aPanel || !tab) {
                log(prefix, 'hosting window / ' + tableName + ' tab not found');
                return deferred.resolve(false).promise();
            }

            var tabId = +tab.getAD_Tab_ID();
            aPanel.onTabChange((panel.windowNo || aPanel.curWindowNo) + '_' + tabId);

            var waited = 0;
            var settled = 0;
            (function poll() {
                try {
                    var cur = aPanel.curTab;
                    var gc = aPanel.curGC;
                    if (cur && gc && typeof gc.navigate === 'function' && +cur.getAD_Tab_ID() === tabId) {
                        if (+cur.getRecord_ID() === record) {
                            if (++settled >= 2) {                      // selected and stayed
                                if (singleRow && !gc.getIsSingleRow()) {
                                    if (aPanel.aSingle && typeof aPanel.showHideViewIcon === 'function') {
                                        aPanel.showHideViewIcon(aPanel.aSingle);
                                    }
                                    gc.switchSingleRow(true);
                                }
                                deferred.resolve(true);
                                return;
                            }
                        } else {
                            settled = 0;
                            var count = +cur.getRowCount() || 0;
                            for (var i = 0; i < count; i++) {
                                if (+cur.getKeyID(i) === record) {
                                    gc.navigate(i);
                                    break;
                                }
                            }
                        }
                    }
                } catch (e) { log(prefix, e); }

                waited += pollMs;
                if (waited < timeoutMs) {
                    setTimeout(poll, pollMs);
                } else {
                    deferred.resolve(false);
                }
            })();
        }
        catch (e) {
            log(prefix, e);
            deferred.resolve(false);
        }

        return deferred.promise();
    };

})(VAS, jQuery);
