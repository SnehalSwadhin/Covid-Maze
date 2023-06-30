# Covid-Maze
This 3D maze game is inspired by the COVID=19 pandemic.
It procedurally generates a maze for the player to traverse, while avoiding the COVID virus that are chasing him. The player must reach his home before being infected completely.
Luckily, there are various water pools and the new Drug which he may use to wash any current infection or cure previous ones respectively.

Can you beat the virus?

## Features implemented
* The world is a 16x16 grid
* The procedural generation of the grid is a backtracking algorithm that assigns each position in the grid with one of the following items
  * Wall
  * Floor
  * Water
  * Drug
  * Virus
* Walls generated have the following conditions to satisfy:
  * At least 1 wall should be placed next to a virus so that the NPC does not spawn in completely open ground
  * There should not be too many consecutive horizontal or vertical walls to avoid long corridors where avoiding a virus would be difficult
* The density of each of these items is checked so that we don't have too many or too few of any one item
* Finally after a grid is generated satisfying all the above constraints, we still need to make sure that a path is possible from the player to his home. Otherwise, we delete the minimum number of wall to make it possible
