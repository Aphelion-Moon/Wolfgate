### Symphony: what a test merge build carries, told at round start and on joining.

symphony-test-merges-active = This server is running { $count ->
    [one] a test merge
   *[other] { $count } test merges
}: { $list }
symphony-test-merge-entry = #{ $number } { $title } (by { $author })
