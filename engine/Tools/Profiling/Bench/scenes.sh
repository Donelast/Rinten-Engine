# The scenes bench.sh measures, sourced by it. One line each: "name|project folder|scene|eye" The project folder is copied into the bench's own work directory before a run, so a run never writes into
# the project it measures. The eye is -perfeye's x,y,z,pitch,yaw - empty for the camera the scene starts with. The folders come from the environment where set, so another machine points them elsewhere.

: "${BENCH_MY_PROJECT:=$HOME/Documents/Rinten Projects/my_project}"
: "${BENCH_MY_PROJECT_1:=$HOME/Documents/Rinten Projects/my_project_1}"
: "${BENCH_MY_PROJECT_2:=$HOME/Documents/Rinten Projects/my_project_2}"
: "${BENCH_TEST_GROUND:=$HOME/Documents/GitHub/Rinten-Test-Ground}"

SCENES=(
	# Next to nothing: sky, ground, a few lit cubes. What the frame costs when the
	# world costs nothing - passes, barriers, the window.
	"minimal|$BENCH_MY_PROJECT_1|scenes/minimal.scene|-6,2,9.5,0,-45"

	# my_project's minimal scene through its player's own camera, no effects - the
	# setup the ~3000 fps of 2026-09-13 was measured in (effects-cost on minimal).
	"minimal-player|$BENCH_MY_PROJECT|scenes/minimal.scene|"

	# A skinned, morphed character with two shadowed lamps, from the player's own
	# camera: posing, the toon surface, lamp shadows.
	"p2|$BENCH_MY_PROJECT_2|scenes/minimal.scene|"

	# The test ground's jungle, from above and from inside: geometry, foliage,
	# the cluster path, the sun's pages.
	"jungle-above|$BENCH_TEST_GROUND|scenes/other/jungle.scene|21.5,52,48,38,22"
	"jungle-inside|$BENCH_TEST_GROUND|scenes/other/jungle.scene|4.3,1.6,23,5,22"
)

# The level-of-detail baseline (ENGINE-PLAN stage Д): BENCH_SET=lod measures these
# instead. Every item of the stage is weighed against them, so they stay as they are.
if [ "${BENCH_SET:-}" = lod ]; then
	SCENES=(
		"jungle-above|$BENCH_TEST_GROUND|scenes/other/jungle.scene|21.5,52,48,38,22"
		"jungle-inside|$BENCH_TEST_GROUND|scenes/other/jungle.scene|4.3,1.6,23,5,22"
		"forest-above|$BENCH_MY_PROJECT_1|scenes/forest_above.scene|"
		"forest-in|$BENCH_MY_PROJECT_1|scenes/forest_in.scene|"
		"graveyard|$BENCH_MY_PROJECT_1|scenes/graveyard.scene|"
		"terrain|$BENCH_TEST_GROUND|scenes/rendering/terrain.scene|"
		"renderers|$BENCH_TEST_GROUND|scenes/rendering/renderers.scene|"
		"character|$BENCH_TEST_GROUND|scenes/animation/character.scene|"
		"curtain-10|$BENCH_MY_PROJECT_1|scenes/lod_d10.scene|"
		"curtain-40|$BENCH_MY_PROJECT_1|scenes/lod_d40.scene|"
		"forest-far|$BENCH_MY_PROJECT_1|scenes/forest_far.scene|"
		# A million pines on 4 x 4 km, static copies - Д7's stand.
		"forest-million|$BENCH_MY_PROJECT_1|scenes/forest_million.scene|0,300,0,12,0"
	)
fi

# The object-count sweep: GameObject cubes (CubeField) and static copies with no
# object behind them (StaticField), 25 600 to a million, from the field's middle.
if [ "${BENCH_SET:-}" = scale ]; then
	SCENES=()
	for kind in stress static; do
		for n in 25k 250k 1m; do
			SCENES+=( "$kind-$n|$BENCH_MY_PROJECT|scenes/${kind}_$n.scene|0,10,0,25,45" )
		done
	done

	# One scene mesh 256 m across, cut fine - the cut of scene meshes by cells.
	SCENES+=( "bigfloor|$BENCH_MY_PROJECT|scenes/bigfloor.scene|0,6,0,15,45" )
fi
