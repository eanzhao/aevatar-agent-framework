// ============================================
// Timeago Compatibility Layer for ABP 9.x
// ============================================
// ABP 9.x migrated from timeago.js to Luxon for date formatting.
// However, legacy code in dom-event-handlers.js still calls $timeagos.timeago().
// This compatibility layer provides the necessary jQuery plugin to prevent errors.

(function (global, factory) {
    typeof exports === 'object' && typeof module !== 'undefined' ? factory(exports) :
    typeof define === 'function' && define.amd ? define(['exports'], factory) :
    (global = typeof globalThis !== 'undefined' ? globalThis : global || self, factory(global.timeago = {}));
})(this, (function (exports) { 'use strict';

    // Provide a dummy timeago object for backward compatibility
    var timeago = {
        format: function(date) {
            console.warn('[Timeago Compat] timeago.format is deprecated in ABP 9.x. Using fallback.');
            return "just now";
        }
    };

    // jQuery plugin: This is what ABP's dom-event-handlers.js actually calls
    if (typeof jQuery !== 'undefined') {
        jQuery.fn.timeago = function() {
            // No-op implementation: ABP 9.x uses Luxon for date formatting
            // The actual date formatting is handled by Luxon elsewhere
            console.log('[Timeago Compat] $.fn.timeago called on', this.length, 'elements (no-op in ABP 9.x)');
            return this; // Return this for jQuery chaining
        };
    }

    exports.timeago = timeago;
    Object.defineProperty(exports, '__esModule', { value: true });

}));

