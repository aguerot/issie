(*
    WaveformSimulationView.fs

    View for waveform simulator in tab
*)

module WaveformSimulationView

open Fulma
open Fable.React
open Fable.React.Props
//open Fable.Core
//open System.IO

open DiagramModelType
open DiagramMessageType
open DiagramStyle
open CommonTypes
open Extractor
open Simulator

let simWireData2Wire wireData = 
    List.rev [0 .. List.length wireData - 1]
    |> List.zip wireData 
    |> List.map (fun (bit, weight) -> match bit with
                                      | SimulatorTypes.Bit.Zero -> bigint 0
                                      | SimulatorTypes.Bit.One -> bigint 2**weight ) //the way I use bigint might be wrong
    |> List.reduce (+)

let extractWaveData
        (simulationIOs : SimulatorTypes.SimulationIO list)
        (graph : SimulatorTypes.SimulationGraph)
        : Sample list =
    let extractWireData (inputs : Map<SimulatorTypes.InputPortNumber, SimulatorTypes.WireData>) : Sample =
        match inputs.TryFind <| SimulatorTypes.InputPortNumber 0 with
        | None -> failwith "what? IO bit not set"
        | Some wireData -> Wire { nBits = uint (List.length wireData)
                                  bitData = simWireData2Wire wireData }
    ([], simulationIOs) ||> List.fold (fun result (ioId, ioLabel, _) ->
        match graph.TryFind ioId with
        | None -> failwithf "what? Could not find io node: %A" (ioId, ioLabel)
        | Some comp -> List.append result [ extractWireData comp.Inputs ]
    )

let simHighlighted (model: Model) dispatch = 
    match model.Diagram.GetCanvasState (), model.CurrProject with
    | None, _ -> ()
    | _, None -> failwith "what? Cannot start a simulation without a project"
    | Some jsState, Some project ->
        let otherComponents =
            project.LoadedComponents
            |> List.filter (fun comp -> comp.Name <> project.OpenFileName)
        (extractState jsState, otherComponents)
        ||> prepareSimulation project.OpenFileName
        |> function
            | Ok simData -> 
                let clkAdvance (sD : SimulatorTypes.SimulationData) = 
                    feedClockTick sD.Graph
                    |> (fun graph -> { sD with Graph = graph
                                               ClockTickNumber = sD.ClockTickNumber+1 })
                let waveNames' = 
                    let extr = List.toArray >> Array.map (fun (_,b,_) -> match b with
                                                                         | SimulatorTypes.ComponentLabel name -> name)
                    (extr simData.Inputs, extr simData.Outputs) ||> Array.append 
                let waveData' : SimTime array =
                    match fst model.WaveSim.viewIndexes with 
                    | start when start = uint 0 -> simData
                    | start -> Array.fold (fun s _ -> clkAdvance s) simData [| 1..int start |]
                    |> (fun sD -> Array.mapFold (fun (s: SimulatorTypes.SimulationData) _ -> 
                            extractWaveData (List.append s.Inputs s.Outputs) s.Graph |> List.toArray, 
                            clkAdvance s) sD [| fst model.WaveSim.viewIndexes..snd model.WaveSim.viewIndexes |] )
                    |> fst

                { model.WaveSim with waveNames = Array.append model.WaveSim.waveNames waveNames'
                                     waveData = (Array.zip model.WaveSim.waveData waveData')
                                                |> Array.map (fun (a,b) -> Array.append a b)
                                     selected = Array.map (fun _ -> false) [| 1..Array.length waveNames' |]
                                                |> Array.append model.WaveSim.selected }
                |> StartWaveSim
                |> dispatch
            | Error _ -> ()

//type functions

let initModel: WaveSimModel =
    { waveData =
          //modify these two signals to change trial data
          let nbits1 = uint32 1
          let nbits2 = uint32 4
          let s1 = [| 0; 0; 0; 0; 1; 0; 1; 1; 1; 1 |]
          let s2 = [| 1; 1; 1; 1; 14; 14; 14; 14; 2; 8 |]

          let s3 =
              [| [| "state1" |]
                 [| "state1" |]
                 [| "state2"; "state1" |]
                 [| "state2" |]
                 [| "state1" |]
                 [| "state2" |]
                 [| "state1" |]
                 [| "state2" |]
                 [| "state1" |]
                 [| "state2" |] |]

          let makeTrialData (nBits1: uint32) (signal1: int []) (nBits2: uint32) signal2 signal3: SimTime [] =
              let makeTimePointData (s1: int, s2: int, s3): SimTime =
                  [| Wire
                      { nBits = nBits1
                        bitData = bigint s1 }
                     Wire
                         { nBits = nBits2
                           bitData = bigint s2 }
                     StateSample s3 |]
              Array.zip signal2 signal3
              |> Array.zip signal1
              |> Array.map ((fun (a, (b, c)) -> (a, b, c)) >> makeTimePointData)

          makeTrialData nbits1 s1 nbits2 s2 s3

      waveNames = [| "try single Bit"; "try bus"; "try states" |]

      selected = [| false; false; false |]

      posParams =
          { sigHeight = 0.5
            hPos = uint 0
            clkWidth = 1.0
            labelWidth = uint 2
            sigThick = 0.02
            boxWidth = uint 8
            boxHeight = uint 15
            spacing = 0.2
            clkThick = 0.025 }

      cursor = uint32 0

      radix = Bin

      viewIndexes = (uint 0, uint 9) }

// SVG functions

let makeLine style = line style []
let makeRect style = rect style []
let makeText style t = text style [ str t ]
let makeSvg style elements = svg style elements

let makeLinePoints style (x1, y1) (x2, y2) =
    line
        (List.append style
             [ X1 x1
               Y1 y1
               X2 x2
               Y2 y2 ]) []

//radix change

let dec2bin (n: bigint) (nBits: uint32): string =
    let folder (state: bigint * char list) (digit: int) =
        if fst state / bigint digit = bigint 1
        then (fst state - bigint digit, List.append (snd state) [ '1' ])
        else (fst state, List.append (snd state) [ '0' ])
    [ float nBits - 1.0 .. (-1.0) .. 0.0 ]
    |> List.map ((fun exp -> 2.0 ** exp) >> (fun f -> int f))
    |> List.fold folder (n, [])
    |> snd
    |> List.toSeq
    |> Seq.map string
    |> String.concat ""

let dec2hex (n: bigint) (nBits: uint32): string =
    let seqPad = [ 1 .. (4 - int nBits % 4) % 4 ] |> List.map (fun _ -> '0')

    let paddedBin =
        dec2bin n nBits
        |> Seq.toList
        |> List.append seqPad

    let fourBitToHexDig fourBit =
        match fourBit with
        | [ '0'; '0'; '0'; '0' ] -> '0'
        | [ '0'; '0'; '0'; '1' ] -> '1'
        | [ '0'; '0'; '1'; '0' ] -> '2'
        | [ '0'; '0'; '1'; '1' ] -> '3'
        | [ '0'; '1'; '0'; '0' ] -> '4'
        | [ '0'; '1'; '0'; '1' ] -> '5'
        | [ '0'; '1'; '1'; '0' ] -> '6'
        | [ '0'; '1'; '1'; '1' ] -> '7'
        | [ '1'; '0'; '0'; '0' ] -> '8'
        | [ '1'; '0'; '0'; '1' ] -> '9'
        | [ '1'; '0'; '1'; '0' ] -> 'A'
        | [ '1'; '0'; '1'; '1' ] -> 'B'
        | [ '1'; '1'; '0'; '0' ] -> 'C'
        | [ '1'; '1'; '0'; '1' ] -> 'D'
        | [ '1'; '1'; '1'; '0' ] -> 'E'
        | [ '1'; '1'; '1'; '1' ] -> 'F'
        | _ -> 'N' // maybe should deal with exception differently

    [ 0 .. 4 .. int nBits - 1 ]
    |> List.map ((fun i -> paddedBin.[i..i + 3]) >> fourBitToHexDig)
    |> List.toSeq
    |> Seq.map string
    |> String.concat ""

let dec2sdec (n: bigint) (nBits: uint32) =
    if (dec2bin n nBits).[0] = '1' then n - bigint (2.0 ** (float nBits)) else n
    |> string

let radixChange (n: bigint) (nBits: uint32) (rad: NumberBase) =
    match rad with
    | Dec -> string n
    | Bin -> dec2bin n nBits
    | Hex -> dec2hex n nBits
    | SDec -> dec2sdec n nBits

//auxiliary functions to the viewer function

let select s ind model =
    { model with
          selected =
              Array.mapi (fun i old ->
                  if i = ind then s else old) model.selected }
    |> StartWaveSim

let makeLabels model = Array.map (fun l -> label [ Class "waveLbl" ] [ str l ]) model.waveNames

let makeSegment (p: PosParamsType) (xInd: int) ((data: Sample), (trans: int * int)) =
    let top = p.spacing
    let bot = top + p.sigHeight - sigLineThick
    let left = float xInd * p.clkWidth
    let right = left + float p.clkWidth

    let makeSigLine = makeLinePoints [ Class "sigLineStyle" ]

    match data with
    | Wire w when w.nBits = uint 1 ->
        let y =
            match w.bitData with
            | n when n = bigint 1 -> top
            | _ -> bot
        // TODO: define DU so that you can't have values other than 0 or 1
        let sigLine = makeSigLine (left, y) (right, y)
        match snd trans with
        | 1 -> [| makeSigLine (right, bot + p.sigThick / 2.0) (right, top - p.sigThick / 2.0) |]
        | 0 -> [||]
        | _ ->
            "What? Transition has value other than 0 or 1" |> ignore
            [||]
        |> Array.append [| sigLine |]
    | _ ->
        let leftInner =
            if fst trans = 1 then left + transLen else left
        let rightInner =
            if snd trans = 1 then right - transLen else right

        let cen = (top + bot) / 2.0

        //make lines
        let topL = makeSigLine (leftInner, top) (rightInner, top)
        let botL = makeSigLine (leftInner, bot) (rightInner, bot)
        let topLeft = makeSigLine (left, cen) (leftInner, top)
        let botLeft = makeSigLine (left, cen) (leftInner, bot)
        let topRight = makeSigLine (right, cen) (rightInner, top)
        let botRight = makeSigLine (right, cen) (rightInner, bot)

        match trans with
        | 1, 1 -> [| topLeft; botLeft; topRight; botRight |]
        | 1, 0 -> [| topLeft; botLeft |]
        | 0, 1 -> [| topRight; botRight |]
        | 0, 0 -> [||]
        | _ ->
            "What? Transition has value other than 0 or 1" |> ignore
            [||]
        |> Array.append [| topL; botL |]
//Probably should put other option for negative number which prints an error

let model2WaveList model: Waveform [] =
    let folder state (simT: SimTime): Waveform [] =
        Array.zip state simT |> Array.map (fun (arr, sample) -> Array.append arr [| sample |])
    let initState = Array.map (fun _ -> [||]) model.waveNames
    Array.fold folder initState model.waveData

let transitions (model: WaveSimModel) = //relies on number of names being correct (= length of elements in waveData)
    let isDiff (ws1, ws2) =
        let folder state (e1, e2) =
            match state, e1 = e2 with
            | 0, true -> 0
            | _ -> 1
        match ws1, ws2 with
        | Wire a, Wire b ->
            if a.bitData = b.bitData then 0 else 1
        | StateSample a, StateSample b when Array.length a = Array.length b -> Array.zip a b |> Array.fold folder 0
        | _ -> 1

    let trans (wave: Waveform) = Array.pairwise wave |> Array.map isDiff
    model2WaveList model |> Array.map trans

// functions for bus labels

let makeGaps trans =
    Array.append trans [| 1 |]
    |> Array.mapFold (fun tot t -> tot, tot + t) 0
    |> fst
    |> Array.indexed
    |> Array.groupBy snd
    |> Array.map (fun (_, gL) ->
        let times = Array.map fst gL
        {| GapLen = Array.max times - Array.min times + 1
           GapStart = Array.min times |})

let busLabels model =
    let gaps2pos (wave: Waveform, gaps) =
        let nSpaces (g: {| GapLen: int; GapStart: int |}) = (g.GapLen / (maxBusValGap + 1) + 2)
        let gapAndInd2Pos (g: {| GapLen: int; GapStart: int |}) i =
            float g.GapStart + float i * float g.GapLen / float (nSpaces g)
        gaps
        |> Array.map (fun (gap: {| GapLen: int; GapStart: int |}) ->
            wave.[gap.GapStart], Array.map (gapAndInd2Pos gap) [| 1 .. nSpaces gap - 1 |])
    (model2WaveList model, Array.map makeGaps (transitions model))
    ||> Array.zip
    |> Array.map gaps2pos

let makeCursVals model =
    let makeCursVal sample =
        match sample with
        | Wire w when w.nBits > uint 1 -> [| radixChange w.bitData w.nBits model.radix |]
        | Wire w -> [| string w.bitData |]
        | StateSample s -> s
        |> Array.map (fun l -> label [ Class "cursVals" ] [ str l ])
    Array.map makeCursVal model.waveData.[int model.cursor]

//container box and clock lines
let backgroundSvg model =
    let p = model.posParams
    let clkLine x =
        makeLinePoints [ Class "clkLineStyle" ] (x, vPos) (x, vPos + float p.sigHeight + float p.spacing)
    let clkLines =
        [| 1 .. 1 .. Array.length model.waveData |] |> Array.map ((fun x -> float x * p.clkWidth) >> clkLine)
    clkLines

let clkRulerSvg (model: WaveSimModel) =
    [| 0 .. (Array.length model.waveData - 1) |]
    |> Array.map (fun i -> makeText (cursRectText model i) (string i))
    |> Array.append [| makeRect (cursRectStyle model) |]
    |> Array.append (backgroundSvg model)
    |> makeSvg (clkRulerStyle model)

let displaySvg (model: WaveSimModel) dispatch =
    let p = model.posParams

    // waveforms
    let waveSvg =
        let addLabel nLabels xInd (i: int) = makeText (inWaveLabel nLabels xInd i model)

        let mapiAndCollect func = Array.mapi func >> Array.collect id

        let valueLabels =
            let lblEl (sample, xIndArr) =
                match sample with
                | Wire w when w.nBits > uint 1 ->
                    Array.map (fun xInd -> addLabel 1 xInd 0 (radixChange w.bitData w.nBits model.radix)) xIndArr
                | StateSample ss ->
                    Array.collect (fun xInd -> Array.mapi (addLabel (Array.length ss) xInd) ss) xIndArr
                | _ -> [||]
            busLabels model |> Array.map (fun row -> Array.collect lblEl row)

        let makeWaveSvg = makeSegment p |> mapiAndCollect
        let padTrans (t: (int * int) []) =
            Array.append (Array.append [| 1, fst t.[0] |] t) [| snd t.[Array.length t - 1], 1 |]
        (model2WaveList model, transitions model)
        ||> Array.zip
        |> Array.map (fun (wave, transRow) ->
            Array.pairwise transRow
            |> padTrans
            |> Array.zip wave)
        |> Array.map makeWaveSvg
        |> Array.zip valueLabels
        |> Array.map (fun (a, b) -> Array.append a b)

    // name and cursor labels of the waveforms
    let labels = makeLabels model
    let cursLabs = makeCursVals model

    let labelCols =
        Array.zip labels cursLabs
        |> Array.mapi (fun i (l, c) ->
            tr [ Class "rowHeight" ]
                [ td [ Class "checkboxCol" ]
                      [ input
                          [ Type "checkbox"
                            Checked model.selected.[i]
                            Style [ Float FloatOptions.Left ]
                            OnChange(fun s -> select s.Checked i model |> dispatch) ] ]
                  td [ Class "waveNamesCol" ] [ l ]
                  td [ Class "cursValsCol" ] c ])

    let waveCol =
        let waveTableRow rowClass cellClass svgClass svgChildren =
            tr rowClass [ td cellClass [ makeSvg svgClass svgChildren ] ]
        let bgSvg = backgroundSvg model
        let cursRectSvg = [| makeRect (cursRectStyle model) |]

        [| waveTableRow [ Class "fullHeight" ] (lwaveCell model) (waveCellSvg model true)
               (Array.append bgSvg cursRectSvg) |]
        |> Array.append
            (Array.map
                (fun wave ->
                    waveTableRow [ Class "rowHeight" ] (waveCell model) (waveCellSvg model false)
                        (Array.collect id [| cursRectSvg; bgSvg; wave |])) waveSvg)
        |> Array.append [| tr [ Class "rowHeight" ] [ td (waveCell model) [ clkRulerSvg model ] ] |]

    (table [ Class "wavesColTableStyle" ] [tbody [] waveCol]), labelCols

// view function helpers

let zoom plus (m: WaveSimModel) =
    let multBy =
        if plus then zoomFactor else 1.0 / zoomFactor
    { m with posParams = { m.posParams with clkWidth = m.posParams.clkWidth * multBy } } |> StartWaveSim

let button style func label =
    Button.button (List.append [ Button.Props [ style ] ] [ Button.OnClick func ]) [ str label ]

let buttonOriginal style func label =
    input
        [ Type "button"
          Value label
          style
          OnClick func ]

let radixString rad =
    match rad with
    | Dec -> "Dec"
    | Bin -> "Bin"
    | Hex -> "Hex"
    | SDec -> "sDec"

let cursorMove increase model =
    match increase, model.cursor, fst model.viewIndexes, snd model.viewIndexes with
    | (true, c, _, fin) when c < fin -> { model with cursor = c + uint 1 }
    | (false, c, start, _) when c > start -> { model with cursor = c - uint 1 }
    | _ -> model
    |> StartWaveSim

let changeCurs newVal model =
    if (fst model.viewIndexes) <= newVal && (snd model.viewIndexes) >= newVal
    then { model with cursor = newVal }
    else model
    |> StartWaveSim

let selectAll s model = { model with selected = Array.map (fun _ -> s) model.selected } |> StartWaveSim

let delSelected model =
    let filtSelected arr =
        Array.zip model.selected arr
        |> Array.filter (fun (sel, _) -> not sel)
        |> Array.map snd
    { model with
          waveData = Array.map filtSelected model.waveData
          waveNames = filtSelected model.waveNames
          selected = filtSelected model.selected }
    |> StartWaveSim

let moveWave model up =
    let lastEl (arr: 'a []) = arr.[Array.length arr - 1]

    let move arr =
        let rev a =
            if up then a else Array.rev a
        rev arr
        |> Array.fold (fun st (bl: {| sel: bool; indxs: int [] |}) ->
            match st with
            | [||] -> [| bl |]
            | _ ->
                if bl.sel then
                    Array.collect id
                        [| st.[0..Array.length st - 2]
                           [| bl |]
                           [| lastEl st |] |]
                else
                    Array.append st [| bl |]) [||]
        |> rev

    let indexes' =
        match Array.length model.selected with
        | len when len < 2 -> [| 0 .. Array.length model.selected - 1 |]
        | _ ->
            Array.indexed model.selected
            |> Array.fold (fun (blocks: {| sel: bool; indxs: int [] |} []) (ind, sel') ->
                match blocks, sel' with
                | [||], s' ->
                    Array.append blocks
                        [| {| sel = s'
                              indxs = [| ind |] |} |]
                | bl, true when (lastEl bl).sel = true ->
                    Array.append blocks.[0..Array.length blocks - 2]
                        [| {| (lastEl blocks) with indxs = Array.append (lastEl blocks).indxs [| ind |] |} |]
                | _, s' ->
                    Array.append blocks
                        [| {| sel = s'
                              indxs = [| ind |] |} |]) [||]
            |> move
            |> Array.collect (fun block -> block.indxs)

    let reorder (arr: 'b []) = Array.map (fun i -> arr.[i]) indexes'

    { model with
          waveData = Array.map (fun sT -> reorder sT) model.waveData
          waveNames = reorder model.waveNames
          selected = reorder model.selected }
    |> StartWaveSim


//[<Emit("__static")>]
//let staticDir() : string = jsNative

//view function of the waveform simulator

let viewWaveSim (fullModel: DiagramModelType.Model) dispatch =
    let model = fullModel.WaveSim
    let p = model.posParams

    [ div [ Style [ Height "7%" ] ]
          [ button (Class "reloadButtonStyle") (fun _ -> ()) "Reload" 

            let radTab rad =
                Tabs.tab [ Tabs.Tab.IsActive (model.radix = rad)
                           Tabs.Tab.Props [Style [ Width "25px"
                                                   Height "30px"] ] ]
                         [ a [ OnClick(fun _ -> StartWaveSim { model with radix = rad } |> dispatch) ]
                         [ str (radixString rad) ] ]
            Tabs.tabs
                [ Tabs.IsBoxed
                  Tabs.IsToggle
                  Tabs.Props [ Style [ Width "100px"
                                       FontSize "80%" 
                                       Float FloatOptions.Right;
                                       Margin "0 10px 0 10px" ] ] ]
                [ radTab Bin
                  radTab Hex
                  radTab Dec
                  radTab SDec ]

            div [ Class "cursor-group" ]
                [ buttonOriginal (Class "button-minus") (fun _ -> cursorMove false model |> dispatch) "-"
                  input
                      [ Id "cursorForm"
                        Step 1
                        SpellCheck false
                        Class "cursor-form"
                        Type "number"
                        Value model.cursor
                        Min(fst model.viewIndexes)
                        Max(snd model.viewIndexes)
                        OnChange(fun c -> changeCurs (uint c.Value) model |> dispatch) ]
                  buttonOriginal (Class "button-plus") (fun _ -> cursorMove true model |> dispatch) "+" ] ] 

      let tableWaves, tableMid = displaySvg model dispatch

      let tableTop =
          [| tr []
                 [ let allSelected =
                     Array.fold (fun state b ->
                         if b then state else false) true model.selected

                   th [ Class "checkboxCol" ]
                       [ input
                           [ Type "checkbox"
                             Checked allSelected
                             OnChange(fun t -> selectAll t.Checked model |> dispatch) ] ]
                   match Array.fold (fun state b ->
                             if b then true else state) false model.selected with
                   | true ->
                       [ button (Class "newWaveButton") (fun _ -> delSelected model |> dispatch) "del"
                         div [ Class "updownDiv" ]
                             [ button (Class "updownButton") (fun _ -> moveWave model true |> dispatch) "▲"
                               button (Class "updownButton") (fun _ -> moveWave model false |> dispatch) "▼" ] ]
                   | false ->
                       [ div [ Style [ WhiteSpace WhiteSpaceOptions.Nowrap ] ]
                             [ button (Class "newWaveButton") (fun _ -> ()) "+"
                               label [ Class "newWaveLabel" ] [ str "Add wave" ] ] ]
                   |> (fun child -> th [ Class "waveNamesCol" ] child)

                   td [ RowSpan(Array.length model.waveNames + 2)
                        Style [ Width "100%" ] ] 
                        [ div [ Style [ Width "100%"
                                        Height "100%"
                                        Position PositionOptions.Relative
                                        OverflowX OverflowOptions.Scroll ] ] [tableWaves] ]

                   th [] [ label [] [ str "" ] ] ] |]

      let tableBot =
          [| tr [ Class "fullHeight" ]
                 [ td [ Class "checkboxCol" ] []
                   td [] [] ] |]

      table [ Class "waveSimTableStyle" ]
          [ tbody [] (Array.collect id [| tableTop; tableMid; tableBot |]) ]

      div [ Class "zoomDiv" ]
          [ button (Class "zoomButtonStyle") (fun _ -> zoom false model |> dispatch) "-"
            label [ Class "hZoomLabel" ] [ str "H Zoom" ]
            //let svgPath = Path.Combine(staticDir(), "hzoom-icon.svg")
            //let svgPath = staticDir() + "\hzoom-icon.svg"
            //embed [ Src svgPath ]
            button (Class "zoomButtonStyle") (fun _ -> zoom true model |> dispatch) "+" ] ] 