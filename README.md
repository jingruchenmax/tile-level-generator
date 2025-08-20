# tile level generator 2.0

A Unity project adapted for WebGL deployment and online study replication.  
This repository documents updates, modifications, and integrations made for research purposes.

---

## Project Updates (Replica Study)

- **Unity version:** 2022.3.52f1  
- **Target platform:** WebGL  
- Added [Button Icons asset by ansdor](https://ansdor.itch.io/button-icons) (credit: ansdor)  
- Font: Resoled Bold (credit: Resoled Bold)  
- Various code changes to ensure WebGL compatibility  

---

## Study-Specific Modifications

- Added WebGL-specific code paths  
- <del>Randomized the order of testing maps</del>  
  - Instead, levels (except the tutorial) require passing the index via a URL flag, e.g., `?levelTag=0` for the tutorial level.
- Implemented logging of player behavior/interaction data for online study integration with **ReVisit**  
- Exposed a function to send unity events from the game to the parent window  

### Example: JavaScript Integration

The following code was added to the JavaScript library:

```javascript
mergeInto(LibraryManager.library, {
  SendTargetClickedMessage: function(jsonPtr) {
    var json = UTF8ToString(jsonPtr);
    window.parent.postMessage({
      type: "game_event",
      payload: JSON.parse(json)
    }, "*");
  }
});
```

## Original Project

[Original Repository](https://github.com/DungeonDigger/tile-level-generator)

This project provides a level generator that can be controlled by a human
user to generate tile-based levels as well as episode data that can
be used to train an AI agent for procedural content generation (PCG)
through apprenticeship learning.


## Requirements

- Unity 2017.2.1f1

## Working with the project

Open the project in Unity and load the scene `Scenes/Editor`. This is
the primary scene used for human-controlled level creation.

## The tile-based generator

This generator is used to create top down dungeon levels in the style of
adventure games like The Legend of Zelda.

Tile-based levels are created by controlling a "digger" character. At the
start of a level creation session, the digger is presented with a grid filled
entirely with blocks. As the digger moves from tile to tile, they "dig out" each
space that they move to, converting it into a traversable open space. Actions
the digger can perform are:

- Movement up, down, left, and right to clear out paths
- Create small, medium, and large square rooms centered around the digger
- Place treasure, enemy, and level exit tiles
- Place keys and locked doors

### Human controls

The following keys can be used for manual control of the digger:

- `w`, `a`, `s`, `d` or `↑`, `←`, `↓`, `→`: movement
- `1`: Create small room (3x3)
- `2`: Create medium room (5x5)
- `3`: Create large room (7x7)
- `j`: Place treasure
- `k`: Place enemy
- `u`: Place key
- `i`: Place locked door (*Note*: Keys must be placed before doors can be
created to ensure playability)
- `p`: Place level exit
- `Esc`: Save and quit the current level generating session
	- Upon quitting, text will appear in a selectable console on the screen
	with the path to files containing the generated tilemap and the recorded
	episode data (a series of states and actions).
- `Insert`: Spawn an AI digger at the starting location which executes a concrete set of actions
- `Delete`: Destroys the AI digger
