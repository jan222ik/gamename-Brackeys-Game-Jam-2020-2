﻿using System.Collections.Generic;
using UnityEngine;


public class Lever : ActivatorBase
{
    private ActivationStateChangeEvent onStateChangeBacking;
    [SerializeField] private bool currentState;
    [SerializeField] private bool inverted;
    [SerializeField] private List<GameTagsEnum> input_tags;
    private List<string> tags;

    private void Awake()
    {
        currentState = inverted;
        if (onStateChangeBacking == null)
        {
            onStateChangeBacking = new ActivationStateChangeEvent();
        }

        tags = new List<string>();
        input_tags.ForEach(it => tags.Add(GameTags.of(it)));
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.LogWarning("Collision");
        if (!isTagInList(other.tag)) return;
        if (!other.isTrigger) return;

            Debug.LogWarning("Collision Matched Tags");
        currentState = !currentState;
        onStateChangeBacking.Invoke(currentState);
    }

    private bool isTagInList(string it) => tags.Contains(it);

    public override ActivationStateChangeEvent onStateChange => onStateChangeBacking;
    public override bool getCurrent() => currentState;
}