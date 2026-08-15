mergeInto(LibraryManager.library, {
    JsBridgeLog: function(msg) {
        var str = UTF8ToString(msg);
        if (typeof dbg === 'function') dbg('[C#] ' + str);
        else console.log('[C#] ' + str);
    }
});
