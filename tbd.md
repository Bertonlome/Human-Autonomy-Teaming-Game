Il faut pouvoir envoyer un robot à un autre robot soit depuis le panneau de droite soit depuis le panneau de contrôle du robot en bas

Il faut créer deux autres maps (on peut envisager un nombre de robot différent par map)

Il faut que l'exploration random soit plus intelligente et puissent être assigné à plusieurs robot en même temps qui ne font pas le même chemin (checker les algos de d'exploration)
Améliorer les déplacements actuellement quand une liste de mouvement est lancé si le robot rencontre un obstacle il va quand même tenter de passer tous les mouvements ce qui casse la trajectoire.

Reprendre l'analyse d'interdépendance et rajouter les nouvelles tâches.

Il faut réfléchir à "comment enseigner HAT", est-ce seulement un support pour s'approprier un scénario ? 

Est-ce que les étudiants doivent déceler des limites sur l'interface du jeu et les corriger ? plutôt orienter OPD

Est-ce que les étudiants doivent trouver de nouvelles fonction pour les robots ?

Il faut vérifier qu'il y a bien un intérêt à l'analyse d'interdépendance pour du teaming ENTRE les robots et avec l'humain.

Utilises-t-on le site internet Dash pour remplir l'analyse d'interdépendance.

Idée : Premièrement, jouez avec les robots, essayez de trouver les tâches et constituer un scénario représentatif (dimenionnant)

Deuxièmement, assess les capacités des robots en jouant ET grâce à la doc (rules of the game)

Troisièmement, trouvez les OPD pour proposer une nouvelle interface

Quatrièmement, faire un sketch de l'interface.

Cinquièmement, les étudiants pourraient utiliser un chemin généré du dash (ex: le plus safe) pour le tester avec leur camarade.

#### Améliorations UI/UX

[ ] difficulté à comprendre ce qui représente la zone de déplacement

[ ] bug already moving sur l'exploration random de l'UAV

[ ] maybe render the stop button only if the robot is moving

[x] il faut changer le gradient, il ne doit jamais y avoir de gros marais barométrique sinon c'est injouable sur une grande map

[x] Plus de temps sur grande map

[x] peut etre click and grab pour deplacer la map avec left click

[x] Reached maxima ne s'affiche pas sur le selected robot UI et il faudrait peut être mettre un glow rouge pour indiquer le moment de reached maxima.

[x] expliquer que lift robot ne marche qu'avec ZQSD

[x] Implémenter un bruit de compteur geiger en plus de anomaly trace toggle.

[ ] Le rover doit pouvoir detecter le monolithe

[ ] gradient descent doit utiliser A* pour eviter d'être bloqué + opérer au rayon du senseur pas juste les cases voisines
