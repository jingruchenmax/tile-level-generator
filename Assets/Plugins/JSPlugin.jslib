mergeInto(LibraryManager.library, {
  SendGameEventMessage: function(jsonPtr) {
    var json = UTF8ToString(jsonPtr);
    window.parent.postMessage({
      type: "game_event",
      payload: JSON.parse(json)
    }, "*");
  }, 
  SendLevelCompleteMessage: function(jsonPtr) {
    var json = UTF8ToString(jsonPtr);
    window.parent.postMessage({
      type: "level_complete",
      payload: JSON.parse(json)
    }, "*");
  }
});
